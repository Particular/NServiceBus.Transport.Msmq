﻿namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using System.Collections.Generic;
    using Particular.Msmq;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;
    using Settings;

    public class When_setting_label_generator : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_receive_the_message_and_label()
        {
            DeleteAudit();
            try
            {
                await Scenario.Define<Context>(c => { c.Id = Guid.NewGuid(); })
                    .WithEndpoint<Endpoint>(b => b.When((session, c) => session.SendLocal(new MyMessage
                    {
                        Id = c.Id
                    })))
                    .Done(c => c.WasCalled)
                    .Run();
                Assert.That(ReadMessageLabel(), Is.EqualTo("MyLabel"));
            }
            finally
            {
                DeleteAudit();
            }
        }

        static void DeleteAudit()
        {
            if (MessageQueue.Exists(auditQueue))
            {
                MessageQueue.Delete(auditQueue);
            }
        }

        static string ReadMessageLabel()
        {
            if (!MessageQueue.Exists(auditQueue))
            {
                return null;
            }
            using (var queue = new MessageQueue(auditQueue))
            {
                using (var message = queue.Receive(TimeSpan.FromSeconds(5)))
                {
                    return message?.Label;
                }
            }
        }

        const string auditQueue = @".\private$\labelAuditQueue";

        public class Context : ScenarioContext
        {
            public bool WasCalled { get; set; }
            public Guid Id { get; set; }
        }

        public class Endpoint : EndpointConfigurationBuilder, IWantToRunBeforeConfigurationIsFinalized
        {
            public Endpoint()
            {
                if (initialized)
                {
                    return;
                }
                initialized = true;
                EndpointSetup<DefaultServer>(c =>
                {
                    c.AuditProcessedMessagesTo("labelAuditQueue");
                    var transportSettings = (MsmqTransport)c.ConfigureTransport();
                    transportSettings.ApplyCustomLabelToOutgoingMessages = GetMessageLabel;
                });
            }

            public void Run(SettingsHolder config)
            {
            }

            string GetMessageLabel(IReadOnlyDictionary<string, string> headers)
            {
                return "MyLabel";
            }

            static bool initialized;
        }

        public class MyMessage : ICommand
        {
            public Guid Id { get; set; }
        }

        public class MyMessageHandler : IHandleMessages<MyMessage>
        {
            readonly Context scenarioContext;
            public MyMessageHandler(Context scenarioContext)
            {
                this.scenarioContext = scenarioContext;
            }

            public Task Handle(MyMessage message, IMessageHandlerContext context)
            {
                if (scenarioContext.Id != message.Id)
                {
                    return Task.FromResult(0);
                }

                scenarioContext.WasCalled = true;

                return Task.FromResult(0);
            }
        }
    }
}