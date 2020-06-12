namespace NServiceBus.Transport.Msmq.AcceptanceTests.TimeToBeReceived
{
    using NServiceBus;
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;
    using System;
    using System.Messaging;
    using System.Threading.Tasks;

    class When_soft_ttbr_enabled : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Native_TimeToBeRecieved_should_not_be_set()
        {
            var spyQueueName = "NativeTtbrSpyQueue";

            var queuePath = $".\\private$\\{spyQueueName}";

            if (MessageQueue.Exists(queuePath))
            {
                MessageQueue.Delete(queuePath);
            }

            MessageQueue.Create(queuePath, true);

            await Scenario.Define<Context>()
                .WithEndpoint<Sender>(endpoint => endpoint
                    .When(async (session, context) =>
                    {
                        var sendOptions = new SendOptions();
                        sendOptions.SetDestination(spyQueueName);
                        await session.Send(new SomeMessage(), sendOptions);
                        context.MessageSent = true;
                    })
                )
                .Done(ctx => ctx.MessageSent)
                .Run();

            using(var queue = new MessageQueue(queuePath))
            {
                queue.MessageReadPropertyFilter.TimeToBeReceived = true;
                var message = queue.Receive();
                Assert.AreNotEqual(TimeSpan.Parse("00:00:05"), message.TimeToBeReceived, "Native TTBR should not be set");
            }
        }

        [Test]
        public async Task NServiceBus_header_should_be_set()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<Sender>(endpoint => endpoint
                    .When(session => session.SendLocal(new SomeMessage()))
                )
                .Done(ctx => ctx.WasReceived)
                .Run(TimeSpan.FromSeconds(20));

            Assert.IsTrue(context.WasReceived, "Message was not received");
            Assert.AreEqual(TimeSpan.Parse("00:00:05"), context.ReceivedTtbr, "NServiceBus header should be set");
        }

        [Test]
        public async Task Message_with_expired_ttbr_should_be_consumed_but_not_processed()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<Sender>(endpoint => endpoint
                    .When(async (session, ctx) =>
                    {
                        var sendOptions = new SendOptions();
                        sendOptions.SetHeader(Headers.TimeSent, DateTimeExtensions.ToWireFormattedString(DateTime.UtcNow.AddSeconds(-10)));
                        sendOptions.RouteToThisEndpoint();
                        await session.Send(new SomeMessage(), sendOptions);
                        ctx.MessageSent = true;
                    })
                )
                .Done(ctx => ctx.MessageSent && HasEmptyInputQueue<Sender>())
                .Run(TimeSpan.FromSeconds(10));

            Assert.IsTrue(context.MessageSent, "Message was not sent");
            Assert.IsFalse(context.WasReceived, "Message should not have been processed");
        }

        bool HasEmptyInputQueue<T>() where T : EndpointConfigurationBuilder
        {
            var endpointName = AcceptanceTesting.Customization.Conventions.EndpointNamingConvention(typeof(T));
            var queuePath = $".\\private$\\{endpointName}";

            try
            {
                using(var queue = new MessageQueue(queuePath))
                {
                    queue.Peek(new TimeSpan(0));
                    return false;
                }
            }
            catch(MessageQueueException e)
            {
                if(e.MessageQueueErrorCode == MessageQueueErrorCode.IOTimeout)
                {
                    return true;
                }
            }

            return false;
        }

        class Sender : EndpointConfigurationBuilder
        {
            public Sender()
            {
                EndpointSetup<DefaultServer>(cfg => cfg.UseTransport<MsmqTransport>().DisableNativeTimeToBeReceived());
                
            }

            public class SomeMessageHandler : IHandleMessages<SomeMessage>
            {
                Context scenarioContext;

                public SomeMessageHandler(Context scenarioContext)
                {
                    this.scenarioContext = scenarioContext;
                }

                public Task Handle(SomeMessage message, IMessageHandlerContext context)
                {
                    if(context.MessageHeaders.TryGetValue(Headers.TimeToBeReceived, out var ttbrString)
                        && TimeSpan.TryParse(ttbrString, out var ttbr))
                    {
                        scenarioContext.ReceivedTtbr = ttbr;
                    }

                    scenarioContext.WasReceived = true;

                    return Task.FromResult(0);
                }
            }
        }

        [TimeToBeReceived("00:00:05")]
        class SomeMessage : IMessage { }

        class Context : ScenarioContext
        {
            public bool MessageSent { get; set; }
            public bool WasReceived { get; set; }
            public TimeSpan? ReceivedTtbr { get; set; }
        }
    }
}
