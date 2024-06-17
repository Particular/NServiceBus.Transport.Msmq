namespace NServiceBus.AcceptanceTests.DelayedDelivery
{
    using System;
    using System.Threading.Tasks;
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.AcceptanceTesting.Customization;
    using NServiceBus.AcceptanceTesting.Support;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NServiceBus.Configuration.AdvancedExtensibility;
    using NServiceBus.Installation;
    using NUnit.Framework;

    class ServerWithInfrastructureWithoutInstallers : IEndpointSetupTemplate
    {
        public IConfigureEndpointTestExecution TransportConfiguration { get; set; } = TestSuiteConstraints.Current.CreateTransportConfiguration();
        public IConfigureEndpointTestExecution PersistenceConfiguration { get; set; } = TestSuiteConstraints.Current.CreatePersistenceConfiguration();

        public async Task<EndpointConfiguration> GetConfiguration(RunDescriptor runDescriptor, EndpointCustomizationConfiguration endpointConfiguration, Func<EndpointConfiguration, Task> configurationBuilderCustomization)
        {
            var builder = new EndpointConfiguration(endpointConfiguration.EndpointName);

            builder.Recoverability()
                .Delayed(delayed => delayed.NumberOfRetries(0))
                .Immediate(immediate => immediate.NumberOfRetries(0));
            builder.SendFailedMessagesTo("error");

            await builder.DefineTransport(TransportConfiguration, runDescriptor, endpointConfiguration).ConfigureAwait(false);
            await builder.DefinePersistence(PersistenceConfiguration, runDescriptor, endpointConfiguration).ConfigureAwait(false);

            await configurationBuilderCustomization(builder).ConfigureAwait(false);

            // scan types at the end so that all types used by the configuration have been loaded into the AppDomain
            builder.TypesToIncludeInScan(endpointConfiguration.GetTypesScopedByTestClass());

            await Installer.Setup(builder).ConfigureAwait(false);

            builder.GetSettings().Set("Installers.Enable", false);

            return builder;
        }
    }

    class When_endpoint_starts_without_installers_and_infrastructure_configured : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Delayed_delivery_works()
        {
            Requires.DelayedDelivery();

            _ = await Scenario.Define<Context>()
                .WithEndpoint<Receiver>()
                .WithEndpoint<Sender>(sender => sender
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
                EndpointSetup<ServerWithInfrastructureWithoutInstallers>(cfg =>
                    cfg.ConfigureRouting().RouteToEndpoint(typeof(DelayedMessage), typeof(Receiver))
                );
            }
        }

        class Receiver : EndpointConfigurationBuilder
        {
            public Receiver()
            {
                EndpointSetup<ServerWithInfrastructureWithoutInstallers>();
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
