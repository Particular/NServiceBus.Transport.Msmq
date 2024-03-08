namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Transactions;
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NServiceBus.Logging;
    using NUnit.Framework;

    class When_deferring_a_message_in_a_handler
    {
        [TestCase(TransportTransactionMode.TransactionScope)]
        [TestCase(TransportTransactionMode.SendsAtomicWithReceive)]
        [TestCase(TransportTransactionMode.ReceiveOnly)]
        [TestCase(TransportTransactionMode.None)]
        public async Task Delay_should_be_applied(TransportTransactionMode transactionMode)
        {
            var now = DateTimeOffset.UtcNow;

            Log.Info($"First message sent at {now:O}");

            var context = await Scenario.Define<Context>()
                .WithEndpoint<Endpoint>(b =>
                {
                    b.CustomConfig(c => c.UseTransport<MsmqTransport>()
                            .Transactions(transactionMode));
                    b.When((session, c) =>
                    {
                        return session.SendLocal(new MyMessage());
                    });
                })
                .Done(c => c.DelayedMessageProcessed)
                .Run();

            Log.Info($"Delayed message processed at {context.DelayedMessageProcessedAt:O}");

            Assert.True(context.DelayedMessageStored);
        }

        public class Context : ScenarioContext
        {
            public bool DelayedMessageProcessed { get; set; }
            public bool DelayedMessageStored { get; set; }
            public DateTimeOffset DelayedMessageProcessedAt { get; set; }
        }

        public class Endpoint : EndpointConfigurationBuilder
        {
            public static TimeSpan Delay = TimeSpan.FromSeconds(10);

            public Endpoint()
            {
                EndpointSetup<DefaultServer>((endpointConfiguration, run) =>
                {
                    var context = (Context)run.ScenarioContext;
                    var transport = endpointConfiguration.UseTransport<MsmqTransport>();

                    var delayedDeliverySettings = transport.NativeDelayedDelivery(new WrapDelayedMessageStore(new SqlServerDelayedMessageStore(ConfigureEndpointMsmqTransport.GetStorageConnectionString()), context));
                });
            }

            public class MyMessageHandler : IHandleMessages<MyMessage>
            {
                public MyMessageHandler(Context testContext)
                {
                    this.testContext = testContext;
                }

                public async Task Handle(MyMessage message, IMessageHandlerContext context)
                {
                    if (message.SentFromHandler)
                    {
                        testContext.DelayedMessageProcessed = true;
                        testContext.DelayedMessageProcessedAt = DateTimeOffset.Now;

                        return;
                    }

                    var options = new SendOptions();
                    options.DelayDeliveryWith(Delay);
                    options.RouteToThisEndpoint();

                    await context.Send(new MyMessage { SentFromHandler = true }, options);
                }

                Context testContext;
            }
        }

        class WrapDelayedMessageStore : IDelayedMessageStore
        {
            readonly IDelayedMessageStore delayedMessageStoreImplementation;
            readonly Context context;

            public WrapDelayedMessageStore(IDelayedMessageStore impl, Context context)
            {
                this.context = context;
                delayedMessageStoreImplementation = impl;
            }

            public Task Initialize(string endpointName, TransportTransactionMode transactionMode, CancellationToken cancellationToken = default)
                => delayedMessageStoreImplementation.Initialize(endpointName, transactionMode, cancellationToken);

            public Task<DateTimeOffset?> Next(CancellationToken cancellationToken = default)
                => delayedMessageStoreImplementation.Next(cancellationToken);

            public Task Store(DelayedMessage entity, CancellationToken cancellationToken = default)
            {
                Transaction.Current.TransactionCompleted += (s, e) => context.DelayedMessageStored = true;
                return delayedMessageStoreImplementation.Store(entity, cancellationToken);
            }

            public Task<bool> IncrementFailureCount(DelayedMessage timeout, CancellationToken cancellationToken = default)
                => delayedMessageStoreImplementation.IncrementFailureCount(timeout, cancellationToken);

            public Task<bool> Remove(DelayedMessage entity, CancellationToken cancellationToken = default)
                => delayedMessageStoreImplementation.Remove(entity, cancellationToken);

            public async Task<DelayedMessage> FetchNextDueTimeout(DateTimeOffset at, CancellationToken cancellationToken = default)
            {
                return await delayedMessageStoreImplementation.FetchNextDueTimeout(at, cancellationToken);
            }
        }
        public class MyMessage : IMessage
        {
            public bool SentFromHandler { get; set; }
        }

        static readonly ILog Log = LogManager.GetLogger<When_deferring_a_message_in_a_handler>();
    }
}
