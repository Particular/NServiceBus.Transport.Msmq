namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using System.Messaging;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_sending_message_with_journaling : NServiceBusAcceptanceTest
    {
        [Test]
        [TestCase(false, false, false)]
        [TestCase(true, true, true)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, null, true)]
        [TestCase(true, null, true)]
        [TestCase(false, null, false)]
        public async Task Verify_dlq_option(bool global, bool? enable, bool result)
        {
            DeleteSpyQueue();
            MessageQueue.Create(sendSpyQueue, true);
            try
            {
                await Scenario.Define<Context>()
                .WithEndpoint<Endpoint>(b => b.CustomConfig(c =>
                {
                    var t = (MsmqTransport)c.ConfigureTransport();
                    if (!global)
                    {
                        t.UseDeadLetterQueue = false;
                    }
                }).When(async (session, c) =>
                {
                    var options = new SendOptions();
                    options.SetDestination(sendSpyEndpoint);
                    if (enable != null)
                    {
                        options.UseDeadLetterQueue(enable: enable.Value);
                    }
                    await session.Send(new MyMessage(), options);
                    await session.Send(new MyMessage(), options);
                    c.WasCalled = true;
                }))
                .Done(c => c.WasCalled)
                .Run();

                using (var queue = new MessageQueue(sendSpyQueue))
                {
                    using (var message = queue.Receive(TimeSpan.FromSeconds(5)))
                    {
                        Assert.AreEqual(result, message?.UseDeadLetterQueue, "UseDeadLetterQueue");
                    }
                }
            }
            finally
            {
                DeleteSpyQueue();
            }
        }

        [Test]
        [TestCase(false, false, false)]
        [TestCase(true, true, true)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, null, true)]
        [TestCase(true, null, true)]
        [TestCase(false, null, false)]
        public async Task Verify_journaling_option(bool global, bool? enable, bool result)
        {
            DeleteSpyQueue();
            MessageQueue.Create(sendSpyQueue, true);
            try
            {
                await Scenario.Define<Context>()
                .WithEndpoint<Endpoint>(b => b.CustomConfig(c =>
                {
                    var t = (MsmqTransport)c.ConfigureTransport();
                    if (global)
                    {
                        t.UseJournalQueue = true;
                    }
                }).When(async (session, c) =>
                {
                    var options = new SendOptions();
                    options.SetDestination(sendSpyEndpoint);
                    if (enable != null)
                    {
                        options.UseJournalQueue(enable: enable.Value);
                    }
                    await session.Send(new MyMessage(), options);
                    await session.Send(new MyMessage(), options);
                    c.WasCalled = true;
                }))
                .Done(c => c.WasCalled)
                .Run();

                using (var queue = new MessageQueue(sendSpyQueue))
                {
                    using (var message = queue.Receive(TimeSpan.FromSeconds(5)))
                    {
                        Assert.AreEqual(result, message?.UseJournalQueue, "UseJournalQueue");
                    }
                }
            }
            finally
            {
                DeleteSpyQueue();
            }
        }


        static void DeleteSpyQueue()
        {
            if (MessageQueue.Exists(sendSpyQueue))
            {
                MessageQueue.Delete(sendSpyQueue);
            }
        }

        static string sendSpyEndpoint = "dlqForJournalingSpy";
        static string sendSpyQueue = $@".\private$\{sendSpyEndpoint}";

        public class Context : ScenarioContext
        {
            public bool WasCalled { get; set; }
        }

        public class Endpoint : EndpointConfigurationBuilder
        {
            public Endpoint()
            {
                EndpointSetup<DefaultServer>(c =>
                {
                });
            }
        }

        public class MyMessage : IMessage
        {
        }
    }
}