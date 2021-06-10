using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        next = new Timer(s => Callback(null), null, -1, -1);
        fetchCircuitBreaker = new RepeatedFailuresOverTimeCircuitBreaker("MsmqDelayedMessageFetch", TimeSpan.FromSeconds(30),
            ex => criticalErrorAction("Failed to fetch due delayed messages from the storage", ex, CancellationToken.None));

        dispatchCircuitBreaker = new RepeatedFailuresOverTimeCircuitBreaker("MsmqDelayedMessageDispatch", TimeSpan.FromSeconds(30),
            ex => criticalErrorAction("Failed fetch due delayed messages from the storage", ex, CancellationToken.None));
    }

    public async void Callback(DateTimeOffset? at)
    {
        bool result = s.Wait(0); // No async needed because of 0 milliseconds.

        if (!result)
        {
            return;
        }

        try
        {
            if (at.HasValue && nextTimeout.HasValue && at.Value >= nextTimeout.Value)
            {
                return;
            }

            while (true)
            {
                DateTime now = DateTime.UtcNow;
                List<TimeoutItem> timeouts;
                try
                {
                    timeouts = await timeoutStorage.FetchDueTimeouts(now).ConfigureAwait(false);
                    fetchCircuitBreaker.Success();
                }
                catch (Exception e)
                {
                    await fetchCircuitBreaker.Failure(e).ConfigureAwait(false);
                    await Task.Delay(1000).ConfigureAwait(false);
                    continue;
                }

                var tasks = timeouts.Select(timeout => HandleDueDelayedMessage(timeout));

                await Task.WhenAll(tasks)
                    .ConfigureAwait(false);

                if (timeouts.Count == 0)
                {
                    DateTimeOffset? next = await timeoutStorage.Next().ConfigureAwait(false);

                    if (next.HasValue)
                    {
                        nextTimeout = next;
                    }
                    else
                    {
                        nextTimeout = null;
                    }

                    TimeSpan sleep = MaxSleepDuration;

                    if (nextTimeout.HasValue)
                    {
                        TimeSpan diff = nextTimeout.Value - DateTime.UtcNow;
                        if (diff < sleep)
                        {
                            sleep = diff;
                        }
                    }

                    if (sleep.Ticks > 0)
                    {
                        Log.DebugFormat("Sleep: {0}", sleep);
                        this.next.Change(sleep, Timeout.InfiniteTimeSpan);
                        break;
                    }
                }
            }
        }
        catch (Exception e)
        {
            // TODO: Something with critical errors
            Console.WriteLine(e);
        }
        finally
        {
            s.Release();
        }
    }

    async Task HandleDueDelayedMessage(TimeoutItem timeout)
    {
        try
        {
            using (var tx = new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
            {
                var transportTransaction = new TransportTransaction();
                transportTransaction.Set(Transaction.Current);

                TimeSpan diff = timeout.Time - DateTime.UtcNow;
                bool success = await timeoutStorage.Remove(timeout).ConfigureAwait(false);

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

                tx.Complete();
            }

            dispatchCircuitBreaker.Success();
        }
        catch (QueueNotFoundException exception)
        {
            await TrySendDelayedMessageToErrorQueue(timeout, exception).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await dispatchCircuitBreaker.Failure(exception).ConfigureAwait(false);
            await timeoutStorage.BumpFailureCount(timeout).ConfigureAwait(false);

            if (timeout.NumberOfRetries > numberOfRetries)
            {
                await TrySendDelayedMessageToErrorQueue(timeout, exception).ConfigureAwait(false);
            }
        }
    }

    async Task TrySendDelayedMessageToErrorQueue(TimeoutItem timeout, Exception exception)
    {
        try
        {
            using (var tx = new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
            {
                var transportTransaction = new TransportTransaction();
                transportTransaction.Set(Transaction.Current);

                bool success = await timeoutStorage.Remove(timeout).ConfigureAwait(false);

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
                await dispatcher.Dispatch(new TransportOperations(transportOperation), transportTransaction, CancellationToken.None).ConfigureAwait(false);

                tx.Complete();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to move delayed message {timeout.Id} to the error queue {errorQueue} after {timeout.NumberOfRetries} failed attempts at dispatching it to the destination", ex);
        }
    }


    public void Start() => _ = next.Change(0, -1);

    public void Stop() => _ = next.Change(-1, -1);

    readonly Dictionary<string, string> faultMetadata;
    readonly SemaphoreSlim s = new SemaphoreSlim(1);
    ITimeoutStorage timeoutStorage;
    MsmqMessageDispatcher dispatcher;
    string errorQueue;
    int numberOfRetries;
    DateTimeOffset? nextTimeout;
    Timer next;
    RepeatedFailuresOverTimeCircuitBreaker fetchCircuitBreaker;
    RepeatedFailuresOverTimeCircuitBreaker dispatchCircuitBreaker;

    static readonly ILog Log = LogManager.GetLogger<TimeoutPoller>();
    static readonly TimeSpan MaxSleepDuration = TimeSpan.FromMinutes(1);
}