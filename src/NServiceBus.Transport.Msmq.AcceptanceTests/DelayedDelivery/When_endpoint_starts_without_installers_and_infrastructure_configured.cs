namespace NServiceBus.AcceptanceTests.DelayedDelivery
{
    using System;
    using System.Threading.Tasks;
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.AcceptanceTesting.Customization;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NServiceBus.Configuration.AdvancedExtensibility;
    using NUnit.Framework;

    class When_endpoint_starts_without_installers_and_infrastructure_configured : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Delayed_delivery_works()
        {
            Requires.DelayedDelivery();

            _ = await Scenario.Define<Context>()
                .WithEndpoint<Receiver>()
                .WithEndpoint<Sender>()
                .Done(c => c.EndpointsStarted)
                .Run();


            _ = await Scenario.Define<Context>()
                .WithEndpoint<Receiver>(s => s.CustomConfig(cfg => cfg.GetSettings().Set("Installers.Enable", false)))
                .WithEndpoint<Sender>(s => s.CustomConfig(cfg => cfg.GetSettings().Set("Installers.Enable", false))
                    .When(
                        (session, _) =>
                        {
                            var sendOptions = new SendOptions();
                            sendOptions.DelayDeliveryWith(TimeSpan.FromSeconds(2));
                            return session.Send(new DelayedMessage(), sendOptions);
                        }))
                .Done(c => c.Done)
                .Run();
        }

        class DelayedMessage : IMessage
        {

        }

        class Sender : EndpointConfigurationBuilder
        {
            public Sender()
            {
                EndpointSetup<DefaultServer>(cfg =>
                    cfg.ConfigureRouting().RouteToEndpoint(typeof(DelayedMessage), typeof(Receiver))
                );
            }
        }

        class Receiver : EndpointConfigurationBuilder
        {
            public Receiver()
            {
                EndpointSetup<DefaultServer>();
            }

            class DelayedMessageHandler : IHandleMessages<DelayedMessage>
            {
                readonly Context scenarioContext;

                public DelayedMessageHandler(Context scenarioContext)
                {
                    this.scenarioContext = scenarioContext;
                }

                public Task Handle(DelayedMessage message, IMessageHandlerContext context)
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
