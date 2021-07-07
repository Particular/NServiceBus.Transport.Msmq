namespace NServiceBus.Transport.Msmq.DelayedDelivery
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Transactions;
    using Logging;
    using Routing;
    using Unicast.Queuing;

    class DueDelayedMessagePoller
    {
        public DueDelayedMessagePoller(
            MsmqMessageDispatcher dispatcher,
            IDelayedMessageStore delayedMessageStore,
            int numberOfRetries,
            Action<string, Exception, CancellationToken> criticalErrorAction,
            string timeoutsErrorQueue,
            Dictionary<string, string> faultMetadata,
            TransportTransactionMode transportTransactionMode,
            TimeSpan timeToTriggerFetchCircuitBreaker,
            TimeSpan timeToTriggerDispatchCircuitBreaker,
            int maximumRecoveryFailuresPerSecond
        )
        {
            txOption = transportTransactionMode == TransportTransactionMode.TransactionScope
                ? TransactionScopeOption.Required
                : TransactionScopeOption.RequiresNew;
            this.delayedMessageStore = delayedMessageStore;
            errorQueue = timeoutsErrorQueue;
            this.faultMetadata = faultMetadata;
            this.numberOfRetries = numberOfRetries;
            this.dispatcher = dispatcher;
            fetchCircuitBreaker = new RepeatedFailuresOverTimeCircuitBreaker("MsmqDelayedMessageFetch", timeToTriggerFetchCircuitBreaker,
                ex => criticalErrorAction("Failed to fetch due delayed messages from the storage", ex, tokenSource?.Token ?? CancellationToken.None));

            dispatchCircuitBreaker = new RepeatedFailuresOverTimeCircuitBreaker("MsmqDelayedMessageDispatch", timeToTriggerDispatchCircuitBreaker,
                ex => criticalErrorAction("Failed to dispatch delayed messages to destination", ex, tokenSource?.Token ?? CancellationToken.None));

            failureHandlingCircuitBreaker = new FailureRateCircuitBreaker("MsmqDelayedMessageFailureHandling", maximumRecoveryFailuresPerSecond,
                ex => criticalErrorAction("Failed to execute error handling for delayed message forwarding", ex, tokenSource?.Token ?? CancellationToken.None));
        }

        public void Start()
        {
            tokenSource = new CancellationTokenSource();
            loopTask = Task.Run(() => Loop(tokenSource.Token));
        }

        public async Task Stop(CancellationToken cancellationToken = default)
        {
            tokenSource.Cancel();
            await loopTask.ConfigureAwait(false);
        }

        public void Signal(DateTimeOffset timeoutTime)
        {
            //If the next timeout is within a minute from now, trigger the poller
            if (DateTimeOffset.UtcNow.Add(MaxSleepDuration) > timeoutTime)
            {
                try
                {
                    breakLoop.Release();
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Loop already spinning
                }
            }
        }

        async Task Loop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Poll(cancellationToken).ConfigureAwait(false);
                    var next = await Next(cancellationToken).ConfigureAwait(false);
                    await Task.WhenAny(Task.Delay(next, cancellationToken), breakLoop.WaitAsync(cancellationToken)).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    Log.Debug("A shutdown was triggered, canceling the Loop operation.");
                    break;
                }
                catch (Exception e)
                {
                    Log.Error("Failed to poll and dispatch due timeouts from storage.", e);
                    //Poll and HandleDueDelayedMessage have their own exception handling logic so any exception here is likely going to be related to the transaction itself
                    await fetchCircuitBreaker.Failure(e, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        async Task<TimeSpan> Next(CancellationToken cancellationToken)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            using (new TransactionScope(TransactionScopeOption.RequiresNew, transactionOptions, TransactionScopeAsyncFlowOption.Enabled))
            {
                var nextDueTimeout = await delayedMessageStore.Next(cancellationToken).ConfigureAwait(false);
                if (nextDueTimeout.HasValue)
                {
                    var duration = nextDueTimeout.Value - now;
                    if (duration < MaxSleepDuration)
                    {
                        return duration;
                    }
                }
                return MaxSleepDuration;
            }
        }

        async Task Poll(CancellationToken cancellationToken)
        {
            DelayedMessage timeout = null;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            do
            {
                try
                {
                    using (var tx = new TransactionScope(TransactionScopeOption.Required, transactionOptions, TransactionScopeAsyncFlowOption.Enabled))
                    {
                        timeout = await delayedMessageStore.FetchNextDueTimeout(now, cancellationToken).ConfigureAwait(false);
                        fetchCircuitBreaker.Success();

                        if (timeout != null)
                        {
                            Dispatch(timeout);
                            var success = await delayedMessageStore.Remove(timeout, cancellationToken).ConfigureAwait(false);
                            if (!success)
                            {
                                Log.WarnFormat("Potential more-than-once dispatch as delayed message already removed from storage.");
                            }
                        }

                        tx.Complete();
                    }
                }
                catch (QueueNotFoundException exception)
                {
                    if (timeout != null)
                    {
                        await TrySendDelayedMessageToErrorQueue(timeout, exception, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    //Shutting down. Bubble up the exception. Will be awaited by AwaitHandleTasks method that catches OperationCanceledExceptions
                    throw;
                }
                catch (Exception exception)
                {
                    failureHandlingCircuitBreaker.Failure(exception); // TODO: Moved here but don't know exactly what its intent was.
                    Log.Error("Failure during timeout polling.", exception);
                    if (timeout != null)
                    {
                        await dispatchCircuitBreaker.Failure(exception, cancellationToken).ConfigureAwait(false);
                        using (var scope = new TransactionScope(TransactionScopeOption.RequiresNew, transactionOptions, TransactionScopeAsyncFlowOption.Enabled))
                        {
                            await delayedMessageStore.IncrementFailureCount(timeout, cancellationToken).ConfigureAwait(false);
                            scope.Complete();
                        }

                        if (timeout.NumberOfRetries > numberOfRetries)
                        {
                            await TrySendDelayedMessageToErrorQueue(timeout, exception, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        await fetchCircuitBreaker.Failure(exception, cancellationToken).ConfigureAwait(false);
                    }
                }
            } while (timeout != null);
        }

        void Dispatch(DelayedMessage timeout)
        {
            TimeSpan diff = DateTimeOffset.UtcNow - new DateTimeOffset(timeout.Time, TimeSpan.Zero);

            Log.DebugFormat("Timeout {0} over due for {1}", timeout.MessageId, diff);

            using (var tx = new TransactionScope(txOption, transactionOptions, TransactionScopeAsyncFlowOption.Enabled))
            {
                var transportTransaction = new TransportTransaction();
                transportTransaction.Set(Transaction.Current);
                dispatcher.DispatchDelayedMessage(
                    timeout.MessageId,
                    timeout.Headers,
                    timeout.Body,
                    timeout.Destination,
                    transportTransaction
                );

                tx.Complete();
            }

            dispatchCircuitBreaker.Success();
        }

        async Task TrySendDelayedMessageToErrorQueue(DelayedMessage timeout, Exception exception, CancellationToken cancellationToken)
        {
            try
            {
                bool success = await delayedMessageStore.Remove(timeout, cancellationToken).ConfigureAwait(false);

                if (!success)
                {
                    // Already dispatched
                    return;
                }

                Dictionary<string, string> headersAndProperties = MsmqUtilities.DeserializeMessageHeaders(timeout.Headers);

                ExceptionHeaderHelper.SetExceptionHeaders(headersAndProperties, exception);

                foreach (KeyValuePair<string, string> pair in faultMetadata)
                {
                    headersAndProperties[pair.Key] = pair.Value;
                }

                Log.InfoFormat("Move {0} to error queue", timeout.MessageId);
                using (var transportTx = new TransactionScope(txOption, transactionOptions, TransactionScopeAsyncFlowOption.Enabled))
                {
                    var transportTransaction = new TransportTransaction();
                    transportTransaction.Set(Transaction.Current);

                    var outgoingMessage = new OutgoingMessage(timeout.MessageId, headersAndProperties, timeout.Body);
                    var transportOperation = new TransportOperation(outgoingMessage, new UnicastAddressTag(errorQueue));
                    await dispatcher.Dispatch(new TransportOperations(transportOperation), transportTransaction, CancellationToken.None)
                        .ConfigureAwait(false);

                    transportTx.Complete();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                //Shutting down
                Log.Debug("Aborted sending delayed message to error queue due to shutdown.");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to move delayed message {timeout.MessageId} to the error queue {errorQueue} after {timeout.NumberOfRetries} failed attempts at dispatching it to the destination", ex);
            }
        }

        static readonly ILog Log = LogManager.GetLogger<DueDelayedMessagePoller>();
        static readonly TimeSpan MaxSleepDuration = TimeSpan.FromMinutes(1);

        readonly Dictionary<string, string> faultMetadata;

        IDelayedMessageStore delayedMessageStore;
        MsmqMessageDispatcher dispatcher;
        string errorQueue;
        int numberOfRetries;
        RepeatedFailuresOverTimeCircuitBreaker fetchCircuitBreaker;
        RepeatedFailuresOverTimeCircuitBreaker dispatchCircuitBreaker;
        FailureRateCircuitBreaker failureHandlingCircuitBreaker;
        Task loopTask;
        CancellationTokenSource tokenSource;
        TransactionScopeOption txOption;
        TransactionOptions transactionOptions = new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted };
        readonly SemaphoreSlim breakLoop = new SemaphoreSlim(1);
    }
}
