namespace NServiceBus.Transport.Msmq.DelayedDelivery
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Channels;
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
                ex => criticalErrorAction("Failed to fetch due delayed messages from the storage", ex, CancellationToken.None));

            dispatchCircuitBreaker = new RepeatedFailuresOverTimeCircuitBreaker("MsmqDelayedMessageDispatch", timeToTriggerDispatchCircuitBreaker,
                ex => criticalErrorAction("Failed to dispatch delayed messages to destination", ex, CancellationToken.None));

            failureHandlingCircuitBreaker = new FailureRateCircuitBreaker("MsmqDelayedMessageFailureHandling", maximumRecoveryFailuresPerSecond, ex => criticalErrorAction("Failed to execute error handling for delayed message forwarding", ex, CancellationToken.None));

            signalQueue = Channel.CreateBounded<bool>(1);
            taskQueue = Channel.CreateBounded<Task>(2);
        }

        public void Start()
        {
            tokenSource = new CancellationTokenSource();
            loopTask = Task.Run(() => Loop(tokenSource.Token));
            completionTask = Task.Run(() => AwaitHandleTasks());
        }

        public async Task Stop(CancellationToken cancellationToken = default)
        {
            tokenSource.Cancel();
            await loopTask.ConfigureAwait(false);
            await completionTask.ConfigureAwait(false);
        }

        public void Signal(DateTimeOffset timeoutTime)
        {
            //If the next timeout is within a minute from now, trigger the poller
            if (DateTimeOffset.UtcNow.Add(MaxSleepDuration) > timeoutTime)
            {
                //If there is something already in the queue we are fine.
                signalQueue.Writer.TryWrite(true);
            }
        }

        /// <summary>
        /// This method does not accept a cancellation token because it need to finish all the tasks that have been started even if cancellation is under way
        /// </summary>
#pragma warning disable PS0018 // A task-returning method should have a CancellationToken parameter unless it has a parameter implementing ICancellableContext
        async Task AwaitHandleTasks()
#pragma warning restore PS0018 // A task-returning method should have a CancellationToken parameter unless it has a parameter implementing ICancellableContext
        {
            while (await taskQueue.Reader.WaitToReadAsync().ConfigureAwait(false)) //if this returns false the channel is completed
            {
                while (taskQueue.Reader.TryRead(out var task))
                {
                    try
                    {
                        await task.ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        failureHandlingCircuitBreaker.Failure(e);
                    }
                }
            }
        }

        async Task Loop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
#pragma warning disable PS0021 // Highlight when a try block passes multiple cancellation tokens
                    try
#pragma warning restore PS0021 // Highlight when a try block passes multiple cancellation tokens
                    {
                        var completionSource = new TaskCompletionSource<DateTimeOffset?>();
                        var handleTask = Poll(completionSource, cancellationToken);
                        await taskQueue.Writer.WriteAsync(handleTask, cancellationToken).ConfigureAwait(false);
                        var nextPoll = await completionSource.Task.ConfigureAwait(false);

                        if (nextPoll.HasValue)
                        {
                            using (var waitCancelled = new CancellationTokenSource())
                            {
                                var combinedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, waitCancelled.Token, cancellationToken);

                                var waitTask = WaitIfNeeded(nextPoll.Value, combinedSource.Token);
                                var signalTask = WaitForSignal(combinedSource.Token);

                                await Task.WhenAny(waitTask, signalTask).ConfigureAwait(false);
                                waitCancelled.Cancel();
                            }
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        //Shutting down
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
            finally
            {
                //No matter what we need to complete the writer so that we can stop gracefully
                taskQueue.Writer.Complete();
            }
        }

        async Task WaitForSignal(CancellationToken cancellationToken)
        {
            try
            {
                await signalQueue.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                //Shutting down
            }
        }

        Task WaitIfNeeded(DateTimeOffset nextPoll, CancellationToken cancellationToken)
        {
            var waitTime = nextPoll - DateTimeOffset.UtcNow;
            if (waitTime > TimeSpan.Zero)
            {
                return Task.Delay(waitTime, cancellationToken);
            }

            return Task.CompletedTask;
        }

        async Task Poll(TaskCompletionSource<DateTimeOffset?> result, CancellationToken cancellationToken)
        {
            DelayedMessage timeout = null;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            try
            {
                using (var tx = new TransactionScope(TransactionScopeOption.Required, transactionOptions, TransactionScopeAsyncFlowOption.Enabled))
                {
                    timeout = await delayedMessageStore.FetchNextDueTimeout(now, cancellationToken).ConfigureAwait(false);
                    fetchCircuitBreaker.Success();

                    if (timeout != null)
                    {
                        result.SetResult(null);
                        await HandleDueDelayedMessage(timeout, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        using (new TransactionScope(TransactionScopeOption.RequiresNew, transactionOptions, TransactionScopeAsyncFlowOption.Enabled))
                        {
                            var nextDueTimeout = await delayedMessageStore.Next(cancellationToken).ConfigureAwait(false);
                            if (nextDueTimeout.HasValue)
                            {
                                result.SetResult(nextDueTimeout);
                            }
                            else
                            {
                                //If no timeouts, wait a while
                                result.SetResult(now.Add(MaxSleepDuration));
                            }
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
                //Shutting down
                throw;
            }
            catch (Exception exception)
            {
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
            finally
            {
                //In case we failed before SetResult
                result.TrySetResult(null);
            }
        }

        async Task HandleDueDelayedMessage(DelayedMessage timeout, CancellationToken cancellationToken)
        {
            TimeSpan diff = DateTimeOffset.UtcNow - new DateTimeOffset(timeout.Time, TimeSpan.Zero);
            var success = await delayedMessageStore.Remove(timeout, cancellationToken).ConfigureAwait(false);

            if (!success)
            {
                // Already dispatched
                return;
            }

            Log.DebugFormat("Timeout {0} over due for {1}", timeout.Id, diff);

            using (var tx = new TransactionScope(txOption, transactionOptions, TransactionScopeAsyncFlowOption.Enabled))
            {
                var transportTransaction = new TransportTransaction();
                transportTransaction.Set(Transaction.Current);
                dispatcher.DispatchDelayedMessage(
                    timeout.Id,
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

                Log.Info($"Dispatch to error queue transaction = {txOption}, Enlist = {txOption == TransactionScopeOption.Required}");
                using (var transportTx = new TransactionScope(txOption, transactionOptions, TransactionScopeAsyncFlowOption.Enabled))
                {
                    var transportTransaction = new TransportTransaction();
                    transportTransaction.Set(Transaction.Current);

                    var outgoingMessage = new OutgoingMessage(timeout.Id, headersAndProperties, timeout.Body);
                    var transportOperation = new TransportOperation(outgoingMessage, new UnicastAddressTag(errorQueue));
                    await dispatcher.Dispatch(new TransportOperations(transportOperation), transportTransaction, CancellationToken.None)
                        .ConfigureAwait(false);

                    transportTx.Complete();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                //Shutting down
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to move delayed message {timeout.Id} to the error queue {errorQueue} after {timeout.NumberOfRetries} failed attempts at dispatching it to the destination", ex);
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
        Task completionTask;
        Channel<bool> signalQueue;
        Channel<Task> taskQueue;
        CancellationTokenSource tokenSource;
        TransactionScopeOption txOption;
        TransactionOptions transactionOptions = new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted };
    }
}
