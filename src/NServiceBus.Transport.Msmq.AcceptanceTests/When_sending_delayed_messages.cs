namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using Logging;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_sending_delayed_messages : NServiceBusAcceptanceTest
    {
        [Test, Explicit]
        [TestCase(TransportTransactionMode.None)]
        [TestCase(TransportTransactionMode.ReceiveOnly)]
        [TestCase(TransportTransactionMode.SendsAtomicWithReceive)]
        [TestCase(TransportTransactionMode.TransactionScope)]
        public async Task Should_work_for_transaction_mode(TransportTransactionMode transactionMode)
        {
            Requires.DelayedDelivery();

            var deliverAt = DateTimeOffset.Now.AddSeconds(10); //To ensure this timeout would never be actually dispatched during this test

            var context = await Scenario.Define<Context>()
                .WithEndpoint<Endpoint>(b =>
                {
                    b.CustomConfig(cfg => cfg.ConfigureTransport<MsmqTransport>().TransportTransactionMode = transactionMode);
                    b.When(async (session, c) =>
                    {
                        var s = Stopwatch.StartNew();
                        var sendTasks = new List<Task>(Context.NrOfDelayedMessages);

                        for (int i = 0; i < Context.NrOfDelayedMessages; i++)
                        {
                            var options = new SendOptions();

                            options.DoNotDeliverBefore(deliverAt);
                            options.RouteToThisEndpoint();

                            sendTasks.Add(session.Send(new MyMessage(), options));
                        }

                        await Task.WhenAll(sendTasks).ConfigureAwait(false);
                        var duration = s.Elapsed;

                        WL($" Sending {Context.NrOfDelayedMessages} delayed messages took {duration}.");
                        WL(" Storing...");
                        c.StoringTimeouts.Wait();
                        WL($" Storing took roughly {s.Elapsed} (include sending)");
                        WL(" Dispatching...");
                        c.DispatchingTimeouts.Wait();
                        var dispatchDuration = DateTimeOffset.UtcNow - deliverAt;
                        WL($" Dispatching took {dispatchDuration}");
                        WL(DateTime.UtcNow + " Done!");
                    }).DoNotFailOnErrorMessages();
                })
                //.Done(c => c.CriticalActionCalled)
                .Run();

            //Assert.False(context.Processed);
            //Assert.True(context.CriticalActionCalled);
        }

        static readonly ILog Log = LogManager.GetLogger<When_sending_delayed_messages>();

        static void WL(string value)
        {
            Log.Info(value);
        }

        public class Context : ScenarioContext
        {
            public const int NrOfDelayedMessages = 1000;
            public readonly CountdownEvent StoringTimeouts = new CountdownEvent(NrOfDelayedMessages);
            public readonly CountdownEvent DispatchingTimeouts = new CountdownEvent(NrOfDelayedMessages);
            public readonly CountdownEvent Processed = new CountdownEvent(NrOfDelayedMessages);
        }

        public class Endpoint : EndpointConfigurationBuilder
        {
            public Endpoint()
            {
                EndpointSetup<DefaultServer>((endpointConfiguration, run) =>
                {
                    var context = (Context)run.ScenarioContext;
                    var transport = endpointConfiguration.ConfigureTransport<MsmqTransport>();
                    //transport.TransportTransactionMode = TransportTransactionMode.ReceiveOnly;

                    var storage = new WrapTimeoutStorage(transport.DelayedDelivery.TimeoutStorage, context);
                    transport.DelayedDelivery = new DelayedDeliverySettings(
                        storage,
                        2,
                        TimeSpan.FromSeconds(5)
                        );
                });
            }

            public class MyMessageHandler : IHandleMessages<MyMessage>
            {
                public MyMessageHandler(Context testContext)
                {
                    this.testContext = testContext;
                }

                public Task Handle(MyMessage message, IMessageHandlerContext context)
                {
                    testContext.Processed.Signal();
                    return Task.FromResult(0);
                }

                Context testContext;
            }
        }

        public class MyMessage : IMessage
        {
        }

        class WrapTimeoutStorage : ITimeoutStorage
        {
            ITimeoutStorage timeoutStorageImplementation;
            Context context;

            public WrapTimeoutStorage(ITimeoutStorage impl, Context context)
            {
                this.context = context;
                timeoutStorageImplementation = impl;
            }

            public Task Initialize(string endpointName, CancellationToken cancellationToken) => timeoutStorageImplementation.Initialize(endpointName, cancellationToken);

            public Task<DateTimeOffset?> Next() => timeoutStorageImplementation.Next();

            public Task Store(TimeoutItem entity)
            {
                System.Transactions.Transaction.Current.TransactionCompleted += (s, e) => context.StoringTimeouts.Signal();
                return timeoutStorageImplementation.Store(entity);
            }

            public Task<bool> Remove(TimeoutItem entity)
            {
                System.Transactions.Transaction.Current.TransactionCompleted += (s, e) => context.DispatchingTimeouts.Signal();
                return timeoutStorageImplementation.Remove(entity);
            }

            public Task<bool> BumpFailureCount(TimeoutItem timeout) => timeoutStorageImplementation.BumpFailureCount(timeout);

            public Task<List<TimeoutItem>> FetchDueTimeouts(DateTimeOffset at) => timeoutStorageImplementation.FetchDueTimeouts(at);
        }

        class FakeTimeoutStorage : ITimeoutStorage
        {
            public Task Initialize(string endpointName, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task<DateTimeOffset?> Next() => Task.FromResult((DateTimeOffset?)DateTimeOffset.UtcNow.AddYears(1));

            public Task Store(TimeoutItem entity) => Task.CompletedTask;

            public Task<bool> Remove(TimeoutItem entity) => Task.FromResult(true);

            public Task<bool> BumpFailureCount(TimeoutItem timeout) => Task.FromResult(true);

            public Task<List<TimeoutItem>> FetchDueTimeouts(DateTimeOffset at) => Task.FromResult(new List<TimeoutItem>(0));
        }
    }
}
