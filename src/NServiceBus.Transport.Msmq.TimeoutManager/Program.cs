using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Dapper;
using NServiceBus;
using NServiceBus.Routing;
using NServiceBus.Transport;
using static AsyncLogger;

class Program
{
    public static IMessageDispatcher dispatcher;
    const string CS = "Server=.;Database=test2;Trusted_Connection=True;";

    static Timer _next;

    static async Task Main()
    {

        var transport = new MsmqTransport { TransportTransactionMode = TransportTransactionMode.ReceiveOnly };

        var infra = await transport.Initialize(
            new HostSettings(
                "particular.timeoutmanager",
                "particular.timeoutmanager",
                new StartupDiagnosticEntries(),
                (s, exception, arg3) => { },
                true),
            new[]
            {
                new ReceiveSettings("1", "particular.timeoutmanager", false, false,
                    "particular.timeoutmanager.errors")
            },
            Array.Empty<string>()
        ).ConfigureAwait(false);

        await infra.Receivers["1"].Initialize(new PushRuntimeSettings(), OnMessage, OnError).ConfigureAwait(false);
        await infra.Receivers["1"].StartReceive().ConfigureAwait(false);

        dispatcher = infra.Dispatcher;

        _next = new Timer(s => Callback(null), null, -1, -1);
        _ = _next.Change(0, -1);

        await CreateTestData()
            .ConfigureAwait(false);

        await Task.Delay(-1).ConfigureAwait(false);
    }

    static async Task CreateTestData()
    {
        var now = DateTimeOffset.UtcNow;

        var s = Stopwatch.StartNew();

        var t = new List<Task>();
        for (int i = 0; i < 1000; i++)
        {
            t.Add(Create(now.AddSeconds(10)));
        }
        await Task.WhenAll(t)
            .ConfigureAwait(false);

        WL(ConsoleColor.Magenta, "Generation took {0}", s.Elapsed);
    }

    static Task Create(DateTimeOffset at)
    {
        return Dispatch(Guid.NewGuid().ToString("n"),
            new Dictionary<string, string>
            {
                {Expire, DateTimeOffsetHelper.ToWireFormattedString(at)}, {RouteExpiredTimeoutTo, "_test"}
            },
            new byte[0],
            "particular.timeoutmanager"
        );
    }

    static Task<ErrorHandleResult> OnError(ErrorContext context, CancellationToken cancellationToken)
    {
        WL(ConsoleColor.Red, "OnError");
        return Task.FromResult(ErrorHandleResult.Handled);
    }

    static async Task OnMessage(MessageContext context, CancellationToken cancellationToken)
    {
        try
        {
            var headers = context.Headers;

            var id = context.NativeMessageId;
            var at = DateTimeOffsetHelper.ToDateTimeOffset(headers[Expire]);
            var destination = headers[RouteExpiredTimeoutTo];
            var body = context.Body;

            headers.Remove(Expire);
            headers.Remove(RouteExpiredTimeoutTo);

            var diff = DateTime.UtcNow - at;

            if (diff.Ticks > 0) // Due
            {
                WL("Overdue on receive ({0})", diff);
                await Dispatch(id, headers, body, destination)
                    .ConfigureAwait(false);
            }
            else
            {
                WL("Due at {0} in {1}", at, diff);
                await Store(id, destination, at, headers, body)
                    .ConfigureAwait(false);

                using (new TransactionScope(TransactionScopeOption.Suppress))
                {
                    Callback(at);
                }
            }
        }
        catch (Exception e)
        {
            WL("Error: {0}", e);
            throw;
        }
    }


    static async Task Store(string id, string destination, DateTimeOffset at, Dictionary<string, string> headers, byte[] body)
    {
        string sql = "INSERT INTO timeout (Id,Destination,Time,Headers,State) Values (@Id,@Destination,@Time,@Headers,@State);";

        using var connection = new SqlConnection(CS);
        var affectedRows = await connection.ExecuteAsync(sql,
            new
            {
                Id = id,
                Destination = destination,
                Time = at,
                Headers = SerializeHeaders(headers),
                State = body
            }).ConfigureAwait(false);
    }

    static byte[] SerializeHeaders(Dictionary<string, string> headers)
    {
        var s = new System.IO.MemoryStream();
        Serializer.Serialize(s, headers);
        return s.ToArray();
    }

    static Dictionary<string, string> DeserializerHeaders(byte[] data)
    {
        return Serializer.Deserialize<Dictionary<string, string>>(new MemoryStream(data, false));
    }

    public static Task Dispatch(string id, Dictionary<string, string> headers, byte[] body, string destination)
    {
        //WL($"Dispatch {id} to {destination}");
        var request = new OutgoingMessage(
            messageId: id,
            headers: headers,
            body: body);

        var operation = new TransportOperation(
            request,
            new UnicastAddressTag(destination));

        return dispatcher.Dispatch(
            outgoingMessages: new TransportOperations(operation),
            transaction: new TransportTransaction()
        );
    }

    static readonly SemaphoreSlim s = new SemaphoreSlim(1);

    static async void Callback(DateTimeOffset? at)
    {
        var result = s.Wait(0);

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
                using var connection = new SqlConnection(CS);
                await connection.OpenAsync().ConfigureAwait(false);

                var timeouts = (await connection.QueryAsync<Timeout>(
                    "Select top 100 * FROM timeout WITH  (updlock, rowlock) WHERE Time<@Time ORDER BY Time, Id",
                    new { Time = now }
                    ).ConfigureAwait(false)).ToList();

                var tasks = timeouts.Select(async timeout =>
                {
                    using var delCon = new SqlConnection(CS);
                    await delCon.OpenAsync().ConfigureAwait(false);
                    using var tx = delCon.BeginTransaction();

                    var diff = timeout.Time - DateTime.UtcNow;
                    var affected = await delCon.ExecuteAsync("DELETE timeout WHERE Id = @Id", new { timeout.Id }, tx)
                        .ConfigureAwait(false);

                    if (affected != 1)
                    {
                        // Already dispatched
                        return;
                    }

                    WL("Timeout {0} over due for {1}", timeout.Id, diff);

                    await Dispatch(
                            timeout.Id,
                            DeserializerHeaders(timeout.Headers),
                            timeout.State,
                            timeout.Destination
                        )
                        .ConfigureAwait(false);
                    tx.Commit();
                });

                await Task.WhenAll(tasks)
                    .ConfigureAwait(false);

                if (timeouts.Count == 0)
                {
                    var next = await connection.ExecuteScalarAsync<DateTime?>("Select top 1 Time FROM timeout ORDER BY Time")
                        .ConfigureAwait(false);

                    if (next.HasValue)
                    {
                        NextTimeout = new DateTimeOffset(next.Value, TimeSpan.Zero);
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
                        WL("Sleep: {0}", sleep);
                        _next.Change(sleep, System.Threading.Timeout.InfiniteTimeSpan);
                        break;
                    }
                }
            }
        }
        finally
        {
            s.Release();
        }
    }

    static DateTimeOffset? NextTimeout;

    static readonly TimeSpan MaxSleepDuration = TimeSpan.FromMinutes(1);

    const string Expire = "NServiceBus.Timeout.Expire";
    const string RouteExpiredTimeoutTo = "NServiceBus.Timeout.RouteExpiredTimeoutTo";
}