namespace NServiceBus.Transport.Msmq.AcceptanceTests.TimeToBeReceived
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using NServiceBus;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    class When_message_with_expired_ttbr_header_is_received : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Message_should_not_be_processed()
        {
            var tcs = new Lazy<CancellationTokenSource>(() => new CancellationTokenSource(TimeSpan.FromSeconds(5)));
            var context = await Scenario.Define<Context>()
                .WithEndpoint<SomeEndpoint>(endpoint => endpoint
                    .CustomConfig(e =>
                    {
                        var transportConfig = (MsmqTransport)e.ConfigureTransport();
                        transportConfig.IgnoreIncomingTimeToBeReceivedHeaders = false;
                    })
                    .When(async (session, ctx) =>
                    {
                        var sendOptions = new SendOptions();
                        sendOptions.RouteToThisEndpoint();
                        sendOptions.SetHeader(Headers.TimeSent, DateTimeOffsetHelper.ToWireFormattedString(DateTimeOffset.UtcNow.AddSeconds(-10)));
                        sendOptions.SetHeader(Headers.TimeToBeReceived, TimeSpan.FromSeconds(5).ToString());

                        await session.Send(new SomeMessage(), sendOptions);
                        ctx.WasSent = true;
                    })
                )
                .Done(c => tcs.Value.IsCancellationRequested) // wait at least 5 seconds to give the endpoint time to process any message
                .Run();

            Assert.IsTrue(context.WasSent, "Message was sent");
            Assert.That(context.WasReceived, Is.False, "Message was processed");
        }

        [Test]
        public async Task Message_should_be_processed_when_ignoringTTBRHeaders()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<SomeEndpoint>(endpoint => endpoint
                    .CustomConfig(e =>
                    {
                        var transportConfig = (MsmqTransport)e.ConfigureTransport();
                        transportConfig.IgnoreIncomingTimeToBeReceivedHeaders = true;
                    })
                    .When(async (session, ctx) =>
                    {
                        var sendOptions = new SendOptions();
                        sendOptions.RouteToThisEndpoint();
                        sendOptions.SetHeader(Headers.TimeSent, DateTimeOffsetHelper.ToWireFormattedString(DateTimeOffset.UtcNow.AddSeconds(-10)));
                        sendOptions.SetHeader(Headers.TimeToBeReceived, TimeSpan.FromSeconds(5).ToString());

                        await session.Send(new SomeMessage(), sendOptions);
                        ctx.WasSent = true;
                    })
                )
                .Done(c => c.WasReceived)
                .Run(TimeSpan.FromSeconds(30));

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
                    return Task.CompletedTask;
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
