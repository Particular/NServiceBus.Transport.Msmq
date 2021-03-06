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

    public class When_deferring_a_message_and_storage_is_unavailable : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_trigger_store_circuit_breaker()
        {
            Requires.DelayedDelivery();

            var delay = TimeSpan.FromSeconds(90); //To ensure this timeout would never be actually dispatched during this test

            var context = await Scenario.Define<Context>()
                .WithEndpoint<Endpoint>(b => b.When((session, c) =>
                {
                    var options = new SendOptions();

                    options.DelayDeliveryWith(delay);
                    options.RouteToThisEndpoint();

                    return session.Send(new MyMessage(), options);
                }).DoNotFailOnErrorMessages())
                .WithEndpoint<ErrorSpy>()
                .Done(c => c.CriticalActionCalled)
                .Run();

            Assert.False(context.Processed);
            Assert.True(context.CriticalActionCalled);
        }

        public class Context : ScenarioContext
        {
            public bool MovedToErrorQueue { get; set; }
            public bool Processed { get; set; }
            public bool CriticalActionCalled { get; set; }
        }

        public class Endpoint : EndpointConfigurationBuilder
        {
            public Endpoint()
            {
                EndpointSetup<DefaultServer>((endpointConfiguration, run) =>
                {
                    var context = (Context)run.ScenarioContext;
                    endpointConfiguration.SendFailedMessagesTo(Conventions.EndpointNamingConvention(typeof(ErrorSpy)));
                    endpointConfiguration.DefineCriticalErrorAction((errorContext, token) =>
                    {
                        context.CriticalActionCalled = true;
                        return Task.CompletedTask;
                    });
                    var transport = endpointConfiguration.ConfigureTransport<MsmqTransport>();
                    transport.DelayedDelivery =
                        new DelayedDeliverySettings(new FaultyDelayedMessageStore(transport.DelayedDelivery.DelayedMessageStore))
                        {
                            NumberOfRetries = 2,
                            TimeToTriggerStoreCircuitBreaker = TimeSpan.FromSeconds(5)
                        };
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

        class FaultyDelayedMessageStore : IDelayedMessageStore
        {
            IDelayedMessageStore impl;

            public FaultyDelayedMessageStore(IDelayedMessageStore impl)
            {
                this.impl = impl;
            }

            public Task Initialize(string endpointName, TransportTransactionMode transactionMode, CancellationToken cancellationToken = default)
                => impl.Initialize(endpointName, transactionMode, cancellationToken);

            public Task<DateTimeOffset?> Next(CancellationToken cancellationToken = default) => impl.Next(cancellationToken);

            public Task Store(DelayedMessage entity, CancellationToken cancellationToken = default)
            {
                throw new Exception("Simulated");
            }

            public Task<bool> Remove(DelayedMessage entity, CancellationToken cancellationToken = default) => impl.Remove(entity, cancellationToken);

            public Task<bool> IncrementFailureCount(DelayedMessage timeout, CancellationToken cancellationToken = default) => impl.IncrementFailureCount(timeout, cancellationToken);

            public Task<DelayedMessage> FetchNextDueTimeout(DateTimeOffset at, CancellationToken cancellationToken = default) => impl.FetchNextDueTimeout(at, cancellationToken);
        }
    }
}
