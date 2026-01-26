namespace NServiceBus.AcceptanceTests.Recoverability
{
    using System;
    using AcceptanceTesting;
    using NUnit.Framework;
    using Transport.Msmq.AcceptanceTests;

    public class When_no_explicit_error_queue_is_configured : NServiceBusAcceptanceTest
    {
        [Test]
        public void Should_not_start_endpoint()
        {
            var ex = Assert.ThrowsAsync<Exception>(async () =>
            {
                await Scenario.Define<Context>()
                    .WithEndpoint<EndpointWithNoErrorQConfig>()
                    .Run();
            });

            Assert.That(ex.Message, Does.Contain("Faults forwarding requires an error queue to be specified"));
        }

        public class EndpointWithNoErrorQConfig : EndpointConfigurationBuilder
        {
            public EndpointWithNoErrorQConfig()
            {
                EndpointSetup<ServerWithNoErrorQueueConfigured>();
            }
        }

        public class Context : ScenarioContext
        {
        }
    }
}