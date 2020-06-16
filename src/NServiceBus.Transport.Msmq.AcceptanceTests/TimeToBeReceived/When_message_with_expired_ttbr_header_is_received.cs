namespace NServiceBus.Transport.Msmq.AcceptanceTests.TimeToBeReceived
{
    using NServiceBus;
    using AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NServiceBus.Configuration.AdvancedExtensibility;
    using NUnit.Framework;
    using System;
    using System.Threading.Tasks;

    class When_message_with_expired_ttbr_header_is_received : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Message_should_not_be_processed()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<SomeEndpoint>(endpoint => endpoint
                    .CustomConfig(c => c.GetSettings().Set("IgnoreIncomingTimeToBeReceivedHeaders", false))
                    .When(async (session, ctx) =>
                    {
                        var sendOptions = new SendOptions();
                        sendOptions.RouteToThisEndpoint();
                        sendOptions.SetHeader(Headers.TimeSent, DateTimeExtensions.ToWireFormattedString(DateTime.UtcNow.AddSeconds(-10)));
                        sendOptions.SetHeader(Headers.TimeToBeReceived, TimeSpan.FromSeconds(5).ToString());

                        await session.Send(new SomeMessage(), sendOptions);
                        ctx.WasSent = true;
                    })
                )
                .Run(TimeSpan.FromSeconds(5));

            Assert.IsTrue(context.WasSent, "Message was sent");
            Assert.IsFalse(context.WasReceived, "Message was processed");
        }

        [Test]
        public async Task Message_should_be_processed_when_ignoringTTBRHeaders()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<SomeEndpoint>(endpoint => endpoint
                    .CustomConfig(c => c.UseTransport<MsmqTransport>().IgnoreIncomingTimeToBeReceivedHeaders())
                    .When(async (session, ctx) =>
                    {
                        var sendOptions = new SendOptions();
                        sendOptions.RouteToThisEndpoint();
                        sendOptions.SetHeader(Headers.TimeSent, DateTimeExtensions.ToWireFormattedString(DateTime.UtcNow.AddSeconds(-10)));
                        sendOptions.SetHeader(Headers.TimeToBeReceived, TimeSpan.FromSeconds(5).ToString());

                        await session.Send(new SomeMessage(), sendOptions);
                        ctx.WasSent = true;
                    })
                )
                .Run(TimeSpan.FromSeconds(5));

            Assert.IsTrue(context.WasSent, "Message was sent");
            Assert.IsTrue(context.WasReceived, "Message was not processed");
        }

        class SomeEndpoint : EndpointConfigurationBuilder
        {
            public SomeEndpoint()
            {
                EndpointSetup<DefaultServer>();
            }

            public class SomeMessageHandler : IHandleMessages<SomeMessage>
            {
                Context scenarioContext;

                public SomeMessageHandler(Context scenarioContext)
                {
                    this.scenarioContext = scenarioContext;
                }

                public Task Handle(SomeMessage message, IMessageHandlerContext context)
                {
                    scenarioContext.WasReceived = true;
                    return TaskEx.CompletedTask;
                }
            }
        }

        class SomeMessage : IMessage
        {
        }

        class Context : ScenarioContext
        {
            public bool WasSent { get; set; }
            public bool WasReceived { get; set; }
        }
    }
}
