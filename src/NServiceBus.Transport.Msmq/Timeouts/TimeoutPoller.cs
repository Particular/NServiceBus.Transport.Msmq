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

class TimeoutPoller
{
    static readonly ILog Log = LogManager.GetLogger<TimeoutPoller>();
    ITimeoutStorage timeoutStorage;
    MsmqMessageDispatcher dispatcher;
    string errorQueue;
    readonly Dictionary<string, string> faultMetadata;
    int numberOfRetries;

    public TimeoutPoller(MsmqMessageDispatcher dispatcher, ITimeoutStorage timeoutStorage, int numberOfRetries, string timeoutsErrorQueue, Dictionary<string, string> faultMetadata)
    {
        this.timeoutStorage = timeoutStorage;
        errorQueue = timeoutsErrorQueue;
        this.faultMetadata = faultMetadata;
        this.numberOfRetries = numberOfRetries;
        this.dispatcher = dispatcher;
        next = new Timer(s => Callback(null), null, -1, -1);
    }

    public async void Callback(DateTimeOffset? at)
    {
        var result = s.Wait(0);// No async needed because of 0 milliseconds.

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
                var now = DateTime.UtcNow;
                {
                    // TODO: What to do when storage down? Critical error!
                    var timeouts = await timeoutStorage.FetchDueTimeouts(now).ConfigureAwait(false);

                    var tasks = timeouts.Select(timeout => HandleDueDelayedMessage(timeout));

                    await Task.WhenAll(tasks)
                        .ConfigureAwait(false);

                    if (timeouts.Count == 0)
                    {
                        var next = await timeoutStorage.Next().ConfigureAwait(false);

                        if (next.HasValue)
                        {
                            nextTimeout = next;
                        }
                        else
                        {
                            nextTimeout = null;
                        }

                        var sleep = MaxSleepDuration;

                        if (nextTimeout.HasValue)
                        {
                            var diff = nextTimeout.Value - DateTime.UtcNow;
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

                // If retries deplete, we want to send the timeout to the error queue and remove it from storage
                var timeoutDestination = timeout.NumberOfRetries > numberOfRetries
                    ? errorQueue
                    : timeout.Destination;

                var diff = timeout.Time - DateTime.UtcNow;
                var success = await timeoutStorage.Remove(timeout).ConfigureAwait(false);

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
                        timeoutDestination,
                        transportTransaction
                    )
                    .ConfigureAwait(false);

                tx.Complete();
            }
        }
        catch (Exception ex)
        {
            await timeoutStorage.BumpFailureCount(timeout).ConfigureAwait(false);

            if (timeout.NumberOfRetries > numberOfRetries)
            {
                await TrySendDelayedMessageToErrorQueue(timeout, ex).ConfigureAwait(false);
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

                var success = await timeoutStorage.Remove(timeout).ConfigureAwait(false);

                if (!success)
                {
                    // Already dispatched
                    return;
                }

                var headersAndProperties = MsmqUtilities.DeserializeMessageHeaders(timeout.Headers);

                ExceptionHeaderHelper.SetExceptionHeaders(headersAndProperties, exception);

                foreach (var pair in faultMetadata)
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

    readonly SemaphoreSlim s = new SemaphoreSlim(1);
    DateTimeOffset? nextTimeout;
    static readonly TimeSpan MaxSleepDuration = TimeSpan.FromMinutes(1);
    Timer next;

    public void Start()
    {
        _ = next.Change(0, -1);
    }

    public void Stop()
    {
        _ = next.Change(-1, -1);
    }
}