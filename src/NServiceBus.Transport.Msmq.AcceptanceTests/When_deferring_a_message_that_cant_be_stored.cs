﻿namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using AcceptanceTesting.Customization;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_deferring_a_message_that_cant_be_stored : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_forward_to_error_queue()
        {
            Requires.DelayedDelivery();

            var delay = TimeSpan.FromSeconds(5); // High value needed as most transports have multi second delay latency by default

            var context = await Scenario.Define<Context>()
                .WithEndpoint<Endpoint>(b => b.When((session, c) =>
                {
                    var options = new SendOptions();

                    options.DelayDeliveryWith(delay);
                    options.RouteToThisEndpoint();

                    return session.Send(new MyMessage(), options);
                }).DoNotFailOnErrorMessages())
                .WithEndpoint<ErrorSpy>()
                .Done(c => c.MovedToErrorQueue)
                .Run();

            Assert.False(context.Processed);
            Assert.True(context.MovedToErrorQueue);
        }

        public class Context : ScenarioContext
        {
            public bool MovedToErrorQueue { get; set; }
            public bool Processed { get; set; }
        }

        public class Endpoint : EndpointConfigurationBuilder
        {
            public Endpoint()
            {
                EndpointSetup<DefaultServer>(endpointConfiguration =>
                {
                    endpointConfiguration.SendFailedMessagesTo(Conventions.EndpointNamingConvention(typeof(ErrorSpy)));
                    var transport = endpointConfiguration.ConfigureTransport<MsmqTransport>();
                    transport.DelayedDelivery = new DelayedDeliverySettings(new FaultyTimeoutStorage(transport.DelayedDelivery.TimeoutStorage), 2);
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

        class ErrorSpy : EndpointConfigurationBuilder
        {
            public ErrorSpy()
            {
                EndpointSetup<DefaultServer>(config => config.LimitMessageProcessingConcurrencyTo(1));
            }

            class MyMessageHandler : IHandleMessages<MyMessage>
            {
                public MyMessageHandler(Context testContext)
                {
                    this.testContext = testContext;
                }

                public Task Handle(MyMessage message, IMessageHandlerContext context)
                {
                    testContext.MovedToErrorQueue = true;
                    return Task.FromResult(0);
                }

                Context testContext;
            }
        }

        public class MyMessage : IMessage
        {
        }

        class FaultyTimeoutStorage : ITimeoutStorage
        {
            ITimeoutStorage impl;

            public FaultyTimeoutStorage(ITimeoutStorage impl)
            {
                this.impl = impl;
            }

            public Task Initialize(string endpointName, TransportTransactionMode transactionMode, CancellationToken cancellationToken) => impl.Initialize(endpointName, transactionMode, cancellationToken);

            public Task<DateTimeOffset?> Next() => impl.Next();

            public Task Store(TimeoutItem entity, TransportTransaction transaction)
            {
                throw new Exception("Simulated");
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