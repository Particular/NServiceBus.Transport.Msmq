namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using System.Threading.Tasks;
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NServiceBus.Logging;
    using NUnit.Framework;

    class When_deferring_a_message_in_a_handler
    {
        [Test]
        public async Task Delay_should_be_applied()
        {
            var now = DateTimeOffset.UtcNow;

            Log.Info($"First message sent at {now:O}");

            var context = await Scenario.Define<Context>()
                .WithEndpoint<Endpoint>(b => b.When((session, c) =>
                {
                    return session.SendLocal(new MyMessage());
                }))
                .Done(c => c.DelayedMessageProcessed)
                .Run();

            Log.Info($"Delayed message processed at {context.DelayedMessageProcessedAt:O}");

            Assert.True(context.DelayedMessageProcessed);
            Assert.GreaterOrEqual(context.DelayedMessageProcessedAt, now.Add(Endpoint.Delay));
        }

        public class Context : ScenarioContext
        {
            public bool DelayedMessageProcessed { get; set; }
            public DateTimeOffset DelayedMessageProcessedAt { get; set; }
        }

        public class Endpoint : EndpointConfigurationBuilder
        {
            public static TimeSpan Delay = TimeSpan.FromSeconds(10);

            public Endpoint()
            {
                EndpointSetup<DefaultServer>((endpointConfiguration, run) =>
                {
                    var context = (Context)run.ScenarioContext;
                    var transport = endpointConfiguration.UseTransport<MsmqTransport>();

                    var delayedDeliverySettings = transport.NativeDelayedDelivery(new SqlServerDelayedMessageStore(ConfigureEndpointMsmqTransport.GetStorageConnectionString()));
                });
            }

            public class MyMessageHandler : IHandleMessages<MyMessage>
            {
                public MyMessageHandler(Context testContext)
                {
                    this.testContext = testContext;
                }

                public async Task Handle(MyMessage message, IMessageHandlerContext context)
                {
                    if (message.SentFromHandler)
                    {
                        testContext.DelayedMessageProcessed = true;
                        testContext.DelayedMessageProcessedAt = DateTimeOffset.Now;

                        return;
                    }

                    var options = new SendOptions();
                    options.DelayDeliveryWith(Delay);
                    options.RouteToThisEndpoint();

                    await context.Send(new MyMessage { SentFromHandler = true }, options);
                }

                Context testContext;
            }
        }

        public class MyMessage : IMessage
        {
            public bool SentFromHandler { get; set; }
        }

        static readonly ILog Log = LogManager.GetLogger<When_deferring_a_message_in_a_handler>();
    }
}
