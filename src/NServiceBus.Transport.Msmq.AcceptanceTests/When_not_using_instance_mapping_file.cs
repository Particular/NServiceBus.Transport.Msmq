﻿namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using AcceptanceTesting.Customization;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NServiceBus.AcceptanceTests;
    using NUnit.Framework;

    public class When_not_using_instance_mapping_file : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_use_logical_endpoint_name_as_address()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<SenderWithoutMappingFile>(e => e.When(c => c.Send(new Message())))
                .WithEndpoint<ReceiverWithoutMappingFile>()
                .Done(c => c.ReceivedMessage)
                .Run();

            Assert.That(context.ReceivedMessage, Is.True);
        }

        public class Context : ScenarioContext
        {
            public bool ReceivedMessage { get; set; }
        }

        public class SenderWithoutMappingFile : EndpointConfigurationBuilder
        {
            public SenderWithoutMappingFile()
            {
                EndpointSetup<DefaultServer>(c =>
                {
                    c.ConfigureRouting()
                        // only configure logical endpoint
                        .RouteToEndpoint(typeof(Message), typeof(ReceiverWithoutMappingFile));
                });
            }
        }

        public class ReceiverWithoutMappingFile : EndpointConfigurationBuilder
        {
            public ReceiverWithoutMappingFile()
            {
                EndpointSetup<DefaultServer>();
            }

            public class MessageHandler : IHandleMessages<Message>
            {
                Context testContext;

                public MessageHandler(Context testContext)
                {
                    this.testContext = testContext;
                }

                public Task Handle(Message message, IMessageHandlerContext context)
                {
                    testContext.ReceivedMessage = true;
                    return Task.FromResult(0);
                }
            }
        }

        public class Message : ICommand
        {
        }
    }
}