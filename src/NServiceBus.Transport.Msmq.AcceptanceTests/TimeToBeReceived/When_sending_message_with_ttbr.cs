namespace NServiceBus.Transport.Msmq.AcceptanceTests.TimeToBeReceived
{
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;
    using System;
    using System.Messaging;
    using System.Threading.Tasks;
    using System.Transactions;

    class When_sending_message_with_ttbr : NServiceBusAcceptanceTest
    {
        string QueuePath(string endpointName) => $".\\private$\\{endpointName}";


        [Test]
        public async Task Uses_native_ttbr_outside_of_transaction()
        {
            var spyQueueName = "NativeTtbrSpyQueue";
            var queuePath = QueuePath(spyQueueName);
            RecreateQueue(queuePath);

            var context = await Scenario.Define<Context>()
                .WithEndpoint<Sender>(endpoint => endpoint
                    .When(async (session, ctx) =>
                    {
                        var sendOptions = new SendOptions();
                        sendOptions.SetDestination(spyQueueName);
                        await session.Send(new SomeMessage(), sendOptions);
                        ctx.MessageSent = true;
                    })
                )
                .Done(ctx => ctx.MessageSent)
                .Run();

            using(var queue = new MessageQueue(queuePath))
            {
                queue.MessageReadPropertyFilter.TimeToBeReceived = true;
                var message = queue.Receive();
                Assert.AreEqual(TimeSpan.Parse("00:00:30"), message.TimeToBeReceived, "Native TTBR should be set");
            }
        }

        [Test]
        public async Task Throws_if_native_ttbr_enabled_inside_transaction()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<Sender>(endpoint => endpoint
                    .When(async (session, ctx) =>
                    {
                        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
                        {
                            try
                            {
                                await session.SendLocal(new SomeMessage());
                            }
                            catch (Exception ex)
                            {
                                ctx.ThrownException = ex;
                            }
                        }
                    })
                )
                .Done(ctx => ctx.ThrownException != null)
                .Run();

            StringAssert.Contains("Sending messages with a custom TimeToBeReceived is not supported on transactional MSMQ", context.ThrownException.Message);
        }

        [Test]
        public async Task Suppress_native_ttbr_if_enabled_inside_transaction()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<Sender>(endpoint => endpoint
                    .CustomConfig(endpointConfiguration => endpointConfiguration.UseTransport<MsmqTransport>().DisableNativeTimeToBeReceivedInTransactions())
                    .When(async (session, ctx) =>
                    {
                        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
                        {
                            await session.SendLocal(new SomeMessage());
                            ctx.MessageSent = true;
                        }
                    })
                )
                .Done(ctx => ctx.MessageSent = true)
                .Run();

            Assert.IsTrue(context.MessageSent, "Message was sent");
        }

        class Sender : EndpointConfigurationBuilder
        {
            public Sender()
            {
                EndpointSetup<DefaultServer>();
            }
        }

        [TimeToBeReceived("00:00:30")]
        class SomeMessage : IMessage
        {

        }

        class Context : ScenarioContext
        {
            public bool MessageSent { get; set; }

            public Exception ThrownException { get; set; }
        }

        static void RecreateQueue(string queuePath)
        {
            if (MessageQueue.Exists(queuePath))
            {
                MessageQueue.Delete(queuePath);
            }

            MessageQueue.Create(queuePath, true);
        }
    }
}
