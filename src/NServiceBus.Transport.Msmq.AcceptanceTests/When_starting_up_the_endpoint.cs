﻿namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System.Linq;
    using System.Security.Principal;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_starting_up_the_endpoint : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_log_warning_as_queue_is_configured_for_everyone()
        {
            var context = await Scenario.Define<ScenarioContext>()
                .WithEndpoint<Endpoint>(b => b.When((session, c) => Task.FromResult(0)))
                .Run();

            var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null).Translate(typeof(NTAccount)).ToString();

            var logItem = context.Logs.FirstOrDefault(item => item.Message.Contains($"[{everyone}]"));
            Assert.That(logItem, Is.Not.Null);
            Assert.That(logItem.Message, Does.Contain("Consider setting appropriate permissions"));
        }

        class Endpoint : EndpointConfigurationBuilder
        {
            public Endpoint()
            {
                EndpointSetup<DefaultServer>();
            }
        }
    }
}