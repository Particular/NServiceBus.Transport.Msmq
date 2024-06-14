namespace NServiceBus.AcceptanceTests.Routing.NativePublishSubscribe
{
    using System.Threading.Tasks;
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NServiceBus.Configuration.AdvancedExtensibility;
    using NUnit.Framework;

    class When_endpoint_starts_without_installers_and_infrastructure_configured : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Native_pub_sub_works()
        {
            Requires.NativePubSubSupport();

            _ = await Scenario.Define<Context>()
                .WithEndpoint<Subscriber>()
                .WithEndpoint<Publisher>()
                .Done(c => c.Done)
                .Run();


            _ = await Scenario.Define<Context>()
                .WithEndpoint<Subscriber>(s => s.CustomConfig(cfg => cfg.GetSettings().Set("Installers.Enable", false)))
                .WithEndpoint<Publisher>(s => s.CustomConfig(cfg => cfg.GetSettings().Set("Installers.Enable", false))
                    .When((session, _) => session.Publish(new EventMessage())))
                .Done(c => c.Done)
                .Run();
        }

        class EventMessage : IEvent
        {
        }

        class Publisher : EndpointConfigurationBuilder
        {
            public Publisher()
            {
                EndpointSetup<DefaultServer>();
            }
        }

        class Subscriber : EndpointConfigurationBuilder
        {
            public Subscriber()
            {
                EndpointSetup<DefaultServer>();
            }

            class DelayedMessageHandler : IHandleMessages<EventMessage>
            {
                readonly Context scenarioContext;

                public DelayedMessageHandler(Context scenarioContext)
                {
                    this.scenarioContext = scenarioContext;
                }

                public Task Handle(EventMessage message, IMessageHandlerContext context)
                {
                    scenarioContext.Done = true;
                    return Task.CompletedTask;
                }
            }
        }

        class Context : ScenarioContext
        {
            public bool Done { get; set; }
        }
    }
}
