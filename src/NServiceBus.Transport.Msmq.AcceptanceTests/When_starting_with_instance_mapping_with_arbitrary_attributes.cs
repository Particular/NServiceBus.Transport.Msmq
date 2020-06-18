namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using System.IO;
    using AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_starting_with_instance_mapping_with_arbitrary_attributes : NServiceBusAcceptanceTest
    {
        [SetUp]
        public void SetupMappingFile()
        {
            File.WriteAllText(mappingFilePath,
@"<endpoints>
    <endpoint name=""someReceiver"">
        <instance discriminator=""1"" someAttribute=""value"" />
    </endpoint>
</endpoints>");
        }

        [TearDown]
        public void DeleteMappingFile()
        {
            File.Delete(mappingFilePath);
        }

        [Test]
        public void Is_ok_by_default()
        {
            Assert.DoesNotThrowAsync(() => Scenario.Define<ScenarioContext>()
                .WithEndpoint<SenderWithFallbackValidation>()
                .Done(ctx => ctx.EndpointsStarted)
                .Run());
        }

        [Test]
        public void Throws_if_strict_schema_validation_enabled()
        {
            var exception = Assert.ThrowsAsync<Exception>(() => Scenario.Define<ScenarioContext>()
                .WithEndpoint<SenderWithStrictValidation>()
                .Done(ctx => ctx.EndpointsStarted)
                .Run());

            Assert.That(exception.Message, Does.Contain("An error occurred while reading the endpoint instance mapping"));
            Assert.That(exception.InnerException, Is.Not.Null);
            Assert.That(exception.InnerException.Message, Does.Contain("The 'someAttribute' attribute is not declared."));
        }

        static string mappingFilePath = Path.Combine(TestContext.CurrentContext.TestDirectory, nameof(When_starting_with_instance_mapping_with_arbitrary_attributes) + ".xml");

        public class SenderWithFallbackValidation : EndpointConfigurationBuilder
        {
            public SenderWithFallbackValidation()
            {
                EndpointSetup<DefaultServer>(c =>
                {
                    var routingSettings = c.UseTransport<MsmqTransport>().Routing();
                    routingSettings.InstanceMappingFile().FilePath(mappingFilePath);
                });
            }
        }

        public class SenderWithStrictValidation : EndpointConfigurationBuilder
        {
            public SenderWithStrictValidation()
            {
                EndpointSetup<DefaultServer>(c =>
                {
                    var routingSettings = c.UseTransport<MsmqTransport>().Routing();
                    routingSettings.InstanceMappingFile().FilePath(mappingFilePath).EnforceStrictSchemaValidation();
                });
            }
        }
    }
}
