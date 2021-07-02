namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_deferring_a_message_properties_should_be_cleaned_up : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Properties_should_be_removed_from_headers()
        {
            Requires.DelayedDelivery();

            var shortDelay = TimeSpan.FromSeconds(10);

            var context = await Scenario.Define<Context>()
                .WithEndpoint<Endpoint>(b => b.When((session, c) =>
                {
                    var options = new SendOptions();

                    options.DelayDeliveryWith(shortDelay);
                    options.RouteToThisEndpoint();

                    return session.Send(new MyMessage(), options);
                }))
                .Done(c => c.Processed)
                .Run();

            Assert.True(context.Processed);
            Assert.True(context.Headers.All(x => !x.Key.StartsWith(MsmqUtilities.PropertyHeaderPrefix)));
        }

        public class Context : ScenarioContext
        {
            public bool Processed { get; set; }
            public IReadOnlyDictionary<string, string> Headers { get; set; }
        }

        public class Endpoint : EndpointConfigurationBuilder
        {
            public Endpoint()
            {
                EndpointSetup<DefaultServer>();
            }

            public class MyMessageHandler : IHandleMessages<MyMessage>
            {
                public MyMessageHandler(Context testContext)
                {
                    this.testContext = testContext;
                }

                public Task Handle(MyMessage message, IMessageHandlerContext context)
                {
                    testContext.Headers = context.MessageHeaders;
                    testContext.Processed = true;
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
