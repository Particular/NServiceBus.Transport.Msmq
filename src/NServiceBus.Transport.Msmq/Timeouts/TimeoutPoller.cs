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

    public TimeoutPoller(ITimeoutStorage timeoutStorage, MsmqMessageDispatcher dispatcher)
    {
        TimeoutStorage = timeoutStorage;
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
            if (at.HasValue && NextTimeout.HasValue && at.Value >= NextTimeout.Value)
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
                        // TODO: try..catch, forwarding to error 
                        using (var tx = new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
                        {
                            var transportTransaction = new TransportTransaction();
                            transportTransaction.Set(Transaction.Current);

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
                                    timeout.Destination,
                                    transportTransaction
                                )
                                .ConfigureAwait(false);

                            tx.Complete();
                        }
                    });

                    await Task.WhenAll(tasks)
                        .ConfigureAwait(false);

                    if (timeouts.Count == 0)
                    {
                        var next = await TimeoutStorage.Next();

                        if (next.HasValue)
                        {
                            NextTimeout = next;
                        }
                        else
                        {
                            NextTimeout = null;
                        }

                        var sleep = MaxSleepDuration;

                        if (NextTimeout.HasValue)
                        {
                            var diff = NextTimeout.Value - DateTime.UtcNow;
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
    DateTimeOffset? NextTimeout;
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