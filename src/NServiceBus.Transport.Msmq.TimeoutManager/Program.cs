using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.Owin.Hosting;
using MyWebApplication;
using NServiceBus;
using NServiceBus.Routing;
using NServiceBus.Transport;

class Program
{
    public static IMessageDispatcher dispatcher;

    static async Task Main()
    {

        GlobalConfiguration.Configuration
            .UseColouredConsoleLogProvider()
            .UseSqlServerStorage("Server=.;Database=hangfire;Trusted_Connection=True;");
        using var hangfireServer = new BackgroundJobServer();

        string baseAddress = "http://localhost:9000/";

        // Start OWIN host 
        using var x = WebApp.Start<Startup>(url: baseAddress);

        var transport = new MsmqTransport();
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


        await Task.Delay(-1).ConfigureAwait(false);
    }

    static Task<ErrorHandleResult> OnError(ErrorContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(ErrorHandleResult.Handled);
    }

    static Task OnMessage(MessageContext context, CancellationToken cancellationToken)
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

            Console.WriteLine($"Schedule {id} to {destination} at {at}");
            _ = BackgroundJob.Schedule(() => Dispatch(id, headers, body, destination), at);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        return Task.CompletedTask;
    }

    public static void Dispatch(string id, Dictionary<string, string> headers, byte[] body, string destination)
    {
        Console.WriteLine($"Dispatch {id} to {destination}");
        var request = new OutgoingMessage(
            messageId: id,
            headers: headers,
            body: body);

        var operation = new TransportOperation(
            request,
            new UnicastAddressTag(destination));

        dispatcher.Dispatch(
                outgoingMessages: new TransportOperations(operation),
                transaction: new TransportTransaction()
            )
            .GetAwaiter().GetResult();
        Console.WriteLine("Dispatched");
    }

    const string Expire = "NServiceBus.Timeout.Expire";
    const string RouteExpiredTimeoutTo = "NServiceBus.Timeout.RouteExpiredTimeoutTo";
}