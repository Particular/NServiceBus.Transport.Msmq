namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using AcceptanceTesting.Customization;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_handling_delayed_delivery_errors_fail : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_trigger_circuit_breaker()
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
                .Done(c => c.CriticalActionCalled)
                .Run();

            Assert.False(context.Processed);
            Assert.False(context.MovedToErrorQueue);
            Assert.True(context.CriticalActionCalled);
            StringAssert.AreEqualIgnoringCase("Failed to execute error handling for delayed message forwarding", context.FailureMessage);
        }

        public class Context : ScenarioContext
        {
            public bool MovedToErrorQueue { get; set; }
            public bool Processed { get; set; }
            public bool CriticalActionCalled { get; set; }
            public string FailureMessage { get; set; }
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
                        context.FailureMessage = errorContext.Error;
                        context.CriticalActionCalled = true;
                        return Task.CompletedTask;
                    });
                    var transport = endpointConfiguration.ConfigureTransport<MsmqTransport>();
                    transport.DelayedDelivery = new DelayedDeliverySettings(new FaultyDelayedMessageStore(transport.DelayedDelivery.DelayedMessageStore), 1, TimeSpan.FromSeconds(5));
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

        class FaultyDelayedMessageStore : IDelayedMessageStore
        {
            IDelayedMessageStore impl;

            public FaultyDelayedMessageStore(IDelayedMessageStore impl)
            {
                this.impl = impl;
            }

            public Task Initialize(string endpointName, TransportTransactionMode transactionMode, CancellationToken cancellationToken) => impl.Initialize(endpointName, transactionMode, cancellationToken);

            public Task<DateTimeOffset?> Next() => impl.Next();

            public Task Store(TimeoutItem entity) => impl.Store(entity);

            public Task<bool> Remove(TimeoutItem entityn)
            {
                throw new Exception("Simulated");
            }

            public Task<bool> IncrementFailureCount(TimeoutItem timeout)
            {
                throw new Exception("Simulated");
            }

            public Task<TimeoutItem> FetchNextDueTimeout(DateTimeOffset at) => impl.FetchNextDueTimeout(at);
        }

        public class MyMessage : IMessage
        {
        }
    }
}
