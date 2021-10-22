namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_deferring_a_message_customizations_should_be_applied : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Customizations_should_be_applied()
        {
            var shortDelay = TimeSpan.FromSeconds(10);

            var context = await Scenario.Define<Context>()
                .WithEndpoint<Endpoint>(b => b.When((session, c) =>
                {
                    var options = new SendOptions();

                    options.DelayDeliveryWith(shortDelay);
                    options.RouteToThisEndpoint();

                    //Non-default options
                    options.UseJournalQueue(true);
                    options.UseDeadLetterQueue(false);

                    return session.Send(new MyMessage(), options);
                }))
                .Done(c => c.Processed)
                .Run();

            Assert.True(context.Processed);
            Assert.True(context.UseJournalQueue);
            Assert.False(context.UseDeadLetterQueue);
        }

        public class Context : ScenarioContext
        {
            public bool Processed { get; set; }
            public bool UseJournalQueue { get; set; }
            public bool UseDeadLetterQueue { get; set; }
        }

        public class Endpoint : EndpointConfigurationBuilder
        {
            public Endpoint()
            {
                EndpointSetup<DefaultServer>((endpointConfiguration, run) =>
                {
                    var context = (Context)run.ScenarioContext;
                    var transport = endpointConfiguration.UseTransport<MsmqTransport>();

                    var delayedDeliverySettings = transport.NativeDelayedDelivery(new SqlServerDelayedMessageStore(ConfigureEndpointMsmqTransport.GetStorageConnectionString()));

                    delayedDeliverySettings.NumberOfRetries = 2;
                    delayedDeliverySettings.TimeToTriggerStoreCircuitBreaker = TimeSpan.FromSeconds(5);
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
                    var nativeMessage = context.Extensions.Get<System.Messaging.Message>();

                    testContext.UseDeadLetterQueue = nativeMessage.UseDeadLetterQueue;
                    testContext.UseJournalQueue = nativeMessage.UseJournalQueue;

                    return Task.FromResult(0);
                }

                Context testContext;
            }
        }

        public class MyMessage : IMessage
        {
        }
    }
}
