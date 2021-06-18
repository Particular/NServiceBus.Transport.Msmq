﻿namespace NServiceBus.Transport.Msmq.AcceptanceTests
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
            Requires.DelayedDelivery();

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
                    var transport = endpointConfiguration.ConfigureTransport<MsmqTransport>();
                    transport.DelayedDelivery = new DelayedDeliverySettings(new TestTimeoutStorage(transport.DelayedDelivery.TimeoutStorage, (Context)run.ScenarioContext));
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

        class TestTimeoutStorage : ITimeoutStorage
        {
            readonly ITimeoutStorage impl;
            readonly Context context;

            public TestTimeoutStorage(ITimeoutStorage impl, Context context)
            {
                this.impl = impl;
                this.context = context;
            }

            public Task Initialize(string endpointName, TransportTransactionMode transactionMode, CancellationToken cancellationToken) => impl.Initialize(endpointName, transactionMode, cancellationToken);

            public async Task<DateTimeOffset?> Next()
            {
                var next = await impl.Next().ConfigureAwait(false);
                context.LongDelayFetched = true;
                return next;
            }

            public async Task Store(TimeoutItem entity, TransportTransaction transaction)
            {
                await impl.Store(entity, transaction).ConfigureAwait(false);
                context.LongTimeoutStored = true;
            }

            public Task<bool> Remove(TimeoutItem entity, TransportTransaction transaction) => impl.Remove(entity, transaction);

            public Task<bool> BumpFailureCount(TimeoutItem timeout) => impl.BumpFailureCount(timeout);

            public Task<TimeoutItem> FetchNextDueTimeout(DateTimeOffset at, TransportTransaction transaction) => impl.FetchNextDueTimeout(at, transaction);
            public TransportTransaction PrepareTransaction() => impl.PrepareTransaction();

            public Task BeginTransaction(TransportTransaction transaction) => impl.BeginTransaction(transaction);

            public Task CommitTransaction(TransportTransaction transaction) => impl.CommitTransaction(transaction);

            public Task ReleaseTransaction(TransportTransaction transaction) => impl.ReleaseTransaction(transaction);
        }
    }
}