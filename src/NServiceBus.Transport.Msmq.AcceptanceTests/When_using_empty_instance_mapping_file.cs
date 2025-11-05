namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using AcceptanceTesting.Customization;
    using Configuration.AdvancedExtensibility;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;
    using Settings;

    public class When_using_empty_instance_mapping_file : NServiceBusAcceptanceTest
    {
        [SetUp]
        public void SetupMappingFile()
        {
            // this can't be static because the conventions are setup in the NServiceBusAcceptanceTest base class
            logicalEndpointName = Conventions.EndpointNamingConvention(typeof(ScaledOutReceiver));

            // e.g. spelling error in endpoint:
            File.WriteAllText(mappingFilePath, "<endpoints></endpoints>");
        }

        [TearDown]
        public void DeleteMappingFile()
        {
            File.Delete(mappingFilePath);
        }

        [Test]
        public async Task Should_send_messages_to_logical_endpoint_address()
        {
            const int MessageCount = 10;

            var context = await Scenario.Define<Context>()
                .WithEndpoint<SenderWithEmptyMappingFile>(e => e.When(async c =>
                {
                    Thread.Sleep(1000);

                    for (var i = 0; i < MessageCount; i++)
                    {
                        await c.Send(new Message());
                    }
                }))
                .WithEndpoint<ScaledOutReceiver>(e => e.CustomConfig(c => c.MakeInstanceUniquelyAddressable("1")))
                .WithEndpoint<ScaledOutReceiver>(e => e.CustomConfig(c => c.MakeInstanceUniquelyAddressable("2")))
                .Done(c => c.MessagesForInstance1 + c.MessagesForInstance2 >= MessageCount)
                .Run();

            Assert.Multiple(() =>
            {
                // it should send messages to the shared queue
                Assert.That(context.MessagesForInstance1, Is.GreaterThanOrEqualTo(1), "MessagesForInstance1 should have at least 1 message");
                Assert.That(context.MessagesForInstance2, Is.GreaterThanOrEqualTo(1), "MessagesForInstance2 should have at least 1 message");
            });
        }

        static string mappingFilePath = Path.Combine(TestContext.CurrentContext.TestDirectory, nameof(When_using_empty_instance_mapping_file) + ".xml");
        static string logicalEndpointName;

        public class Context : ScenarioContext
        {
            public int MessagesForInstance1;
            public int MessagesForInstance2;
        }

        public class SenderWithEmptyMappingFile : EndpointConfigurationBuilder
        {
            public SenderWithEmptyMappingFile()
            {
                EndpointSetup<DefaultServer>(c =>
                {
                    var routingSettings = new RoutingSettings<MsmqTransport>(c.GetSettings());
                    routingSettings.RouteToEndpoint(typeof(Message), logicalEndpointName);
                    routingSettings.InstanceMappingFile().FilePath(mappingFilePath);
                });
            }
        }

        public class ScaledOutReceiver : EndpointConfigurationBuilder
        {
            public ScaledOutReceiver()
            {
                EndpointSetup<DefaultServer>();
            }

            public class MessageHandler : IHandleMessages<Message>
            {
                public MessageHandler(Context testContext, IReadOnlySettings settings)
                {
                    this.testContext = testContext;
                    this.settings = settings;
                }

                public Task Handle(Message message, IMessageHandlerContext context)
                {
                    var instanceDiscriminator = settings.Get<string>("EndpointInstanceDiscriminator");
                    if (instanceDiscriminator == "1")
                    {
                        Interlocked.Increment(ref testContext.MessagesForInstance1);
                    }
                    if (instanceDiscriminator == "2")
                    {
                        Interlocked.Increment(ref testContext.MessagesForInstance2);
                    }

                    return Task.CompletedTask;
                }

                Context testContext;
                IReadOnlySettings settings;
            }
        }

        public class Message : ICommand
        {
        }
    }
}