namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NUnit.Framework;

    public class When_no_explicit_error_queue_configured_for_sendonly_endpoint_with_timeouts : NServiceBusAcceptanceTest
    {
        [Test]
        public void Should_throw_exception()
        {
            Assert.ThrowsAsync<Exception>(async () =>
            {
                await Scenario.Define<Context>()
                    .WithEndpoint<EndpointWithNoErrorQConfig>()
                    .Run();
            });
        }

        public class EndpointWithNoErrorQConfig : EndpointConfigurationBuilder
        {
            public EndpointWithNoErrorQConfig()
            {
                EndpointSetup<ServerWithNoErrorQueueConfigured>(endpointConfiguration =>
                {
                    endpointConfiguration.SendOnly();
                });
            }
        }

        public class Context : ScenarioContext
        {
        }
    }
}