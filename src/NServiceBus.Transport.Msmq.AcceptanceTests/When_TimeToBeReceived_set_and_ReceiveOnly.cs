﻿namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_TimeToBeReceived_set_and_ReceiveOnly : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_not_throw()
        {
            var context = await Scenario.Define<Context>()
                    .WithEndpoint<TtbrEndpoint>(e => e
                        .When(s => s.SendLocal(new StartMessage())))
                    .Done(c => c.ReceivedTtbrMessage)
                    .Run();

            Assert.Multiple(() =>
            {
                Assert.That(context.ReceivedTtbrMessage, Is.True);
                Assert.That(context.TimeToBeReceived, Is.EqualTo("00:00:15"));
            });
        }

        class Context : ScenarioContext
        {
            public bool ReceivedTtbrMessage { get; set; }
            public string TimeToBeReceived { get; set; }
        }

        class TtbrEndpoint : EndpointConfigurationBuilder
        {
            public TtbrEndpoint()
            {
                EndpointSetup<DefaultServer>(c => c
                    .ConfigureTransport().TransportTransactionMode = TransportTransactionMode.ReceiveOnly);
            }

            class StartMessageHandler : IHandleMessages<StartMessage>
            {
                public Task Handle(StartMessage message, IMessageHandlerContext context)
                {
                    return context.SendLocal(new TtbrMessage());
                }
            }

            class TtbrMessageHandler : IHandleMessages<TtbrMessage>
            {
                Context testContext;

                public TtbrMessageHandler(Context testContext)
                {
                    this.testContext = testContext;
                }

                public Task Handle(TtbrMessage ttbrMessage, IMessageHandlerContext context)
                {
                    testContext.ReceivedTtbrMessage = true;
                    testContext.TimeToBeReceived = context.MessageHeaders[Headers.TimeToBeReceived];
                    return Task.CompletedTask;
                }
            }
        }

        public class StartMessage : ICommand
        {
        }

        [TimeToBeReceived("00:00:15")]
        public class TtbrMessage : ICommand
        {
        }
    }
}