using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Transactions;
using NServiceBus.Logging;
using NServiceBus.Routing;
using NServiceBus.Transport;
using NServiceBus.Transport.Msmq;
using NServiceBus.Unicast.Queuing;

class TimeoutPoller
{
    public TimeoutPoller(MsmqMessageDispatcher dispatcher, ITimeoutStorage timeoutStorage, int numberOfRetries, Action<string, Exception, CancellationToken> criticalErrorAction, string timeoutsErrorQueue, Dictionary<string, string> faultMetadata)
    {
        this.timeoutStorage = timeoutStorage;
        errorQueue = timeoutsErrorQueue;
        this.faultMetadata = faultMetadata;
        this.numberOfRetries = numberOfRetries;
        this.dispatcher = dispatcher;
        fetchCircuitBreaker = new RepeatedFailuresOverTimeCircuitBreaker("MsmqDelayedMessageFetch", TimeSpan.FromSeconds(30),
            ex => criticalErrorAction("Failed to fetch due delayed messages from the storage", ex, CancellationToken.None));

        dispatchCircuitBreaker = new RepeatedFailuresOverTimeCircuitBreaker("MsmqDelayedMessageDispatch", TimeSpan.FromSeconds(30),
            ex => criticalErrorAction("Failed to dispatch delayed messages to destination", ex, CancellationToken.None));

        failureHandlingCircuitBreaker = new FailureRateCircuitBreaker("MsmqDelayedMessageFailureHandling", 1, ex => criticalErrorAction("Failed to execute error handling for delayed message forwarding", ex, CancellationToken.None));

        signalQueue = Channel.CreateBounded<bool>(1);
        taskQueue = Channel.CreateBounded<Task>(2);
    }

    public void Start()
    {
        tokenSource = new CancellationTokenSource();
        loopTask = Task.Run(() => Loop(tokenSource.Token));
        completionTask = Task.Run(() => AwaitHandleTasks());
    }

    public async Task Stop()
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

    async Task AwaitHandleTasks()
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
                try
                {
                    var completionSource = new TaskCompletionSource<DateTimeOffset?>();
                    var handleTask = Poll(completionSource);
                    await taskQueue.Writer.WriteAsync(handleTask, cancellationToken).ConfigureAwait(false);
                    var nextPoll = await completionSource.Task.ConfigureAwait(false);

                    if (nextPoll.HasValue)
                    {
                        using (var waitCancelled = new CancellationTokenSource())
                        {
                            var combinedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, waitCancelled.Token);

                            var waitTask = WaitIfNeeded(nextPoll.Value, combinedSource.Token);
                            var signalTask = WaitForSignal(combinedSource.Token);

                            await Task.WhenAny(waitTask, signalTask).ConfigureAwait(false);
                            waitCancelled.Cancel();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    //Poll and HandleDueDelayedMessage have their own exception handling logic so any exception here is likely going to be related to the transaction itself
                    await fetchCircuitBreaker.Failure(e).ConfigureAwait(false);
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
        catch (OperationCanceledException)
        {
            //Ignore
        }
    }

    Task WaitIfNeeded(DateTimeOffset nextPoll, CancellationToken cancellationToken)
    {
        var waitTime = nextPoll - DateTimeOffset.UtcNow;
        if (waitTime > TimeSpan.Zero)
        {
            //return Task.Delay(1000, cancellationToken);
            return Task.Delay(waitTime, cancellationToken);
        }

        return Task.CompletedTask;
    }

    async Task Poll(TaskCompletionSource<DateTimeOffset?> result)
    {
        TimeoutItem timeout = null;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        try
        {
            var tx = timeoutStorage.CreateTransaction();
            try
            {
                await timeoutStorage.BeginTransaction(tx).ConfigureAwait(false);

                timeout = await timeoutStorage.FetchNextDueTimeout(now, tx).ConfigureAwait(false);
                fetchCircuitBreaker.Success();

                if (timeout != null)
                {
                    result.SetResult(null);
                    await HandleDueDelayedMessage(timeout, tx).ConfigureAwait(false);
                }
                else
                {
                    var nextDueTimeout = await timeoutStorage.Next().ConfigureAwait(false);
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

                await timeoutStorage.CommitTransaction(tx).ConfigureAwait(false);
            }
            finally
            {
                await timeoutStorage.DisposeTransaction(tx).ConfigureAwait(false);
            }
        }
        catch (QueueNotFoundException exception)
        {
            if (timeout != null)
            {
                await TrySendDelayedMessageToErrorQueue(timeout, exception).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            if (timeout != null)
            {
                await dispatchCircuitBreaker.Failure(exception).ConfigureAwait(false);
                await timeoutStorage.BumpFailureCount(timeout).ConfigureAwait(false);

                if (timeout.NumberOfRetries > numberOfRetries)
                {
                    await TrySendDelayedMessageToErrorQueue(timeout, exception).ConfigureAwait(false);
                }
            }
            else
            {
                await fetchCircuitBreaker.Failure(exception).ConfigureAwait(false);
            }
        }
        finally
        {
            //In case we failed before SetResult
            result.TrySetResult(null);
        }
    }

    async Task HandleDueDelayedMessage(TimeoutItem timeout, TransportTransaction transaction)
    {
        var transportTransaction = new TransportTransaction();
        transportTransaction.Set(Transaction.Current);

        TimeSpan diff = DateTimeOffset.UtcNow - new DateTimeOffset(timeout.Time, TimeSpan.Zero);
        var success = await timeoutStorage.Remove(timeout, transaction).ConfigureAwait(false);

        if (!success)
        {
            // Already dispatched
            return;
        }

        Log.DebugFormat("Timeout {0} over due for {1}", timeout.Id, diff);

        await dispatcher.DispatchDelayedMessage(
                timeout.Id,
                timeout.Headers,
                timeout.State,
                timeout.Destination,
                transportTransaction
            )
            .ConfigureAwait(false);

        dispatchCircuitBreaker.Success();
    }

    async Task TrySendDelayedMessageToErrorQueue(TimeoutItem timeout, Exception exception)
    {
        try
        {
            var tx = timeoutStorage.CreateTransaction();
            try
            {
                await timeoutStorage.BeginTransaction(tx).ConfigureAwait(false);

                bool success = await timeoutStorage.Remove(timeout, tx).ConfigureAwait(false);

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

                var outgoingMessage = new OutgoingMessage(timeout.Id, headersAndProperties, timeout.State);
                var transportOperation = new TransportOperation(outgoingMessage, new UnicastAddressTag(errorQueue));
                await dispatcher.Dispatch(new TransportOperations(transportOperation), tx, CancellationToken.None).ConfigureAwait(false);

                await timeoutStorage.CommitTransaction(tx).ConfigureAwait(false);
            }
            finally
            {
                await timeoutStorage.DisposeTransaction(tx).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to move delayed message {timeout.Id} to the error queue {errorQueue} after {timeout.NumberOfRetries} failed attempts at dispatching it to the destination", ex);
        }
    }

    readonly Dictionary<string, string> faultMetadata;
    ITimeoutStorage timeoutStorage;
    MsmqMessageDispatcher dispatcher;
    string errorQueue;
    int numberOfRetries;
    RepeatedFailuresOverTimeCircuitBreaker fetchCircuitBreaker;
    RepeatedFailuresOverTimeCircuitBreaker dispatchCircuitBreaker;
    FailureRateCircuitBreaker failureHandlingCircuitBreaker;

    Task loopTask;
    Task completionTask;

    static readonly ILog Log = LogManager.GetLogger<TimeoutPoller>();
    static readonly TimeSpan MaxSleepDuration = TimeSpan.FromMinutes(1);
    Channel<bool> signalQueue;
    Channel<Task> taskQueue;
    CancellationTokenSource tokenSource;
}
