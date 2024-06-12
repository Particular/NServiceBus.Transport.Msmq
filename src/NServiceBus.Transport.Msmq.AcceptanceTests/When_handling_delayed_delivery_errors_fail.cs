namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using System.Diagnostics;
    using System.Runtime.ExceptionServices;
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

            try
            {
                AppDomain.CurrentDomain.FirstChanceException += HandleFirstChangeException;

                var delay = TimeSpan.FromSeconds(5); // High value needed as most transports have multi second delay latency by default

                var context = await Scenario.Define<Context>()
                    .WithEndpoint<Endpoint>(b => b.When(async (session, c) =>
                    {
                        //HINT: Two messages will cause the failure circuit breaker to be triggered sooner
                        for (int i = 0; i < 2; i++)
                        {
                            var options = new SendOptions();

                            options.DelayDeliveryWith(delay);
                            options.RouteToThisEndpoint();

                            await session.Send(new MyMessage(), options);
                        }
                    }).DoNotFailOnErrorMessages())
                    .WithEndpoint<ErrorSpy>()
                    .Done(c => c.CriticalActionCalled)
                    .Run(TimeSpan.FromSeconds(180));

                Assert.False(context.Processed, nameof(context.Processed)); // When remove fails dispatch should be rolled back
                Assert.False(context.MovedToErrorQueue, nameof(context.MovedToErrorQueue));
                Assert.True(context.CriticalActionCalled, nameof(context.CriticalActionCalled));
                StringAssert.AreEqualIgnoringCase("Failed to execute error handling for delayed message forwarding", context.FailureMessage);
            }
            finally
            {
                AppDomain.CurrentDomain.FirstChanceException -= HandleFirstChangeException;
            }
        }

        static void HandleFirstChangeException(object sender, FirstChanceExceptionEventArgs args)
            => Console.WriteLine("\nFirstChanceException\n" + args.Exception.ToString().Replace("\n", "\n\t") + new StackTrace(args.Exception, 1).ToString().Replace("\n", "\n\t"));

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
                    transport.DelayedDelivery =
                        new DelayedDeliverySettings(new FaultyDelayedMessageStore((IDelayedMessageStoreWithInfrastructure)transport.DelayedDelivery.DelayedMessageStore))
                        {
                            NumberOfRetries = 1,
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

        class FaultyDelayedMessageStore : IDelayedMessageStoreWithInfrastructure
        {
            IDelayedMessageStoreWithInfrastructure impl;

            public FaultyDelayedMessageStore(IDelayedMessageStoreWithInfrastructure impl)
            {
                this.impl = impl;
            }

            public Task Initialize(string endpointName, TransportTransactionMode transactionMode, CancellationToken cancellationToken = default) => impl.Initialize(endpointName, transactionMode, cancellationToken);

            public Task SetupInfrastructure(CancellationToken cancellationToken = default) => impl.SetupInfrastructure(cancellationToken);

            public Task<DateTimeOffset?> Next(CancellationToken cancellationToken = default) => impl.Next(cancellationToken);

            public Task Store(DelayedMessage entity, CancellationToken cancellationToken = default) => impl.Store(entity, cancellationToken);

            public Task<bool> Remove(DelayedMessage entity, CancellationToken cancellationToken = default)
            {
                throw new Exception("Simulated");
            }

            public Task<bool> IncrementFailureCount(DelayedMessage timeout, CancellationToken cancellationToken = default)
            {
                throw new Exception("Simulated");
            }

            public Task<DelayedMessage> FetchNextDueTimeout(DateTimeOffset at, CancellationToken cancellationToken = default) => impl.FetchNextDueTimeout(at, cancellationToken);
        }

        public class MyMessage : IMessage
        {
        }
    }
}
