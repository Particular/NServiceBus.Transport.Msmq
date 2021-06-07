using NServiceBus;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using NServiceBus.Logging;
using NServiceBus.Transport;
using NServiceBus.Transport.Msmq;

class TimeoutPoller
{
    ILog Log = LogManager.GetLogger<TimeoutPoller>();
    ITimeoutStorage TimeoutStorage;
    MsmqMessageDispatcher Dispatcher;
    MsmqAddress errorQueue;
    int nrOfRetries;

    public TimeoutPoller(DelayedDeliverySettings delayedDeliverySettings, MsmqMessageDispatcher dispatcher)
    {
        TimeoutStorage = delayedDeliverySettings.TimeoutStorage;
        errorQueue = delayedDeliverySettings.GetErrorQueueAddress();
        nrOfRetries = delayedDeliverySettings.NrOfRetries;
        Dispatcher = dispatcher;
        _next = new Timer(s => Callback(null), null, -1, -1);
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
                    var timeouts = await TimeoutStorage.FetchDueTimeouts(now);

                    var tasks = timeouts.Select(async timeout =>
                    {
                        try
                        {
                            using (var tx = new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
                            {
                                var transportTransaction = new TransportTransaction();
                                transportTransaction.Set(Transaction.Current);

                                // If retries deplete, we want to send the timeout to the error queue and remove it from storage
                                var timeoutDestination = timeout.NrOfRetries > nrOfRetries
                                    ? errorQueue.ToString()
                                    : timeout.Destination;

                                var diff = timeout.Time - DateTime.UtcNow;
                                var success = await TimeoutStorage.Remove(timeout).ConfigureAwait(false);

                                if (!success)
                                {
                                    // Already dispatched
                                    return;
                                }

                                Log.DebugFormat("Timeout {0} over due for {1}", timeout.Id, diff);

                                await Dispatcher.Dispatch(
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
                        catch (Exception)
                        {
                            await TimeoutStorage.BumpFailureCount(timeout).ConfigureAwait(false);
                        }
                    });

                    await Task.WhenAll(tasks)
                        .ConfigureAwait(false);

                    if (timeouts.Count == 0)
                    {
                        var next = await TimeoutStorage.Next();

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
                            _next.Change(sleep, System.Threading.Timeout.InfiniteTimeSpan);
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

    readonly SemaphoreSlim s = new SemaphoreSlim(1);
    DateTimeOffset? nextTimeout;
    static readonly TimeSpan MaxSleepDuration = TimeSpan.FromMinutes(1);
    Timer _next;

    public void Start()
    {
        _ = _next.Change(0, -1);
    }

    public void Stop()
    {
        _ = _next.Change(-1, -1);
    }
}