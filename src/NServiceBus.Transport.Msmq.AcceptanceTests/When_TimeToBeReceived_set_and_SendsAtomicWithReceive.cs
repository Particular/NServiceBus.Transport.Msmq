﻿namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using AcceptanceTesting.Support;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_TimeToBeReceived_set_and_SendsAtomicWithReceive : NServiceBusAcceptanceTest
    {
        [Test]
        public void Should_throw_on_send()
        {
            var exception = Assert.ThrowsAsync<MessageFailedException>(async () =>
                await Scenario.Define<Context>()
                    .WithEndpoint<Endpoint>(b => b.When(async (session, c) => await session.SendLocal(new MyMessage())))
                    .Done(c => c.HandlerInvoked)
                    .Run());

            Assert.AreEqual(1, exception.ScenarioContext.FailedMessages.Count);
            StringAssert.EndsWith(
                "Sending messages with a custom TimeToBeReceived is not supported on transactional MSMQ.",
                exception.FailedMessage.Exception.Message);
        }

        public class Context : ScenarioContext
        {
            public bool HandlerInvoked { get; set; }
        }

        public class Endpoint : EndpointConfigurationBuilder
        {
            public Endpoint()
            {
                EndpointSetup<DefaultServer>((config, context) =>
                {
                    config.ConfigureTransport().TransportTransactionMode =
                        TransportTransactionMode.SendsAtomicWithReceive;
                });
            }
            public class MyMessageHandler : IHandleMessages<MyMessage>
            {
                readonly Context scenarioContext;
                public MyMessageHandler(Context scenarioContext)
                {
                    this.scenarioContext = scenarioContext;
                }

                public Task Handle(MyMessage message, IMessageHandlerContext context)
                {
                    scenarioContext.HandlerInvoked = true;
                    return context.SendLocal(new MyTimeToBeReceivedMessage());
                }
            }
        }

        public class MyMessage : IMessage
        {
        }

        [TimeToBeReceived("00:01:00")]
        public class MyTimeToBeReceivedMessage : IMessage
        {
        }
    }
}
