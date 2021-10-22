namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_deferring_a_message_while_poller_sleeps : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_forward_to_error_queue()
        {
            var longDelay = TimeSpan.FromSeconds(120); //Too long for the test to pass
            var shortDelay = TimeSpan.FromSeconds(5);

            var context = await Scenario.Define<Context>()
                .WithEndpoint<Endpoint>(b => b.When((session, c) =>
                {
                    var options = new SendOptions();

                    options.DelayDeliveryWith(longDelay);
                    options.RouteToThisEndpoint();

                    return session.Send(new MyMessage(), options);
                }).When(c => c.LongDelayFetched && c.LongTimeoutStored, session =>
                {
                    var options = new SendOptions();

                    options.DelayDeliveryWith(shortDelay);
                    options.RouteToThisEndpoint();

                    return session.Send(new MyMessage(), options);

                }))
                .Done(c => c.Processed)
                .Run();

            Assert.True(context.Processed);
        }

        public class Context : ScenarioContext
        {
            public bool Processed { get; set; }
            public bool LongDelayFetched { get; set; }
            public bool LongTimeoutStored { get; set; }
        }

        public class Endpoint : EndpointConfigurationBuilder
        {
            public Endpoint()
            {
                EndpointSetup<DefaultServer>((endpointConfiguration, run) =>
                {
                    var transport = endpointConfiguration.UseTransport<MsmqTransport>();
                    var delayedDeliverySettings = transport.NativeDelayedDelivery(new TestDelayedMessageStore(new SqlServerDelayedMessageStore(ConfigureEndpointMsmqTransport.GetStorageConnectionString()), (Context)run.ScenarioContext));
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
                    testContext.Processed = true;
                    return Task.FromResult(0);
                }

                Context testContext;
            }
        }

        public class MyMessage : IMessage
        {
        }

        class TestDelayedMessageStore : IDelayedMessageStore
        {
            readonly IDelayedMessageStore impl;
            readonly Context context;

            public TestDelayedMessageStore(IDelayedMessageStore impl, Context context)
            {
                this.impl = impl;
                this.context = context;
            }

            public Task Initialize(string endpointName, TransportTransactionMode transactionMode, CancellationToken cancellationToken = default)
                => impl.Initialize(endpointName, transactionMode, cancellationToken);

            public async Task<DateTimeOffset?> Next(CancellationToken cancellationToken = default)
            {
                var next = await impl.Next(cancellationToken).ConfigureAwait(false);
                context.LongDelayFetched = true;
                return next;
            }

            public async Task Store(DelayedMessage entity, CancellationToken cancellationToken = default)
            {
                await impl.Store(entity, cancellationToken).ConfigureAwait(false);
                context.LongTimeoutStored = true;
            }

            public Task<bool> Remove(DelayedMessage entity, CancellationToken cancellationToken = default) => impl.Remove(entity, cancellationToken);

            public Task<bool> IncrementFailureCount(DelayedMessage timeout, CancellationToken cancellationToken = default) => impl.IncrementFailureCount(timeout, cancellationToken);

            public Task<DelayedMessage> FetchNextDueTimeout(DateTimeOffset at, CancellationToken cancellationToken = default) => impl.FetchNextDueTimeout(at, cancellationToken);
        }
    }
}
