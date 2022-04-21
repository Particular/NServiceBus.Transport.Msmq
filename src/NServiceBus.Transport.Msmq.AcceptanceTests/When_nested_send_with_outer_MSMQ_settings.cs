namespace NServiceBus.AcceptanceTests
{
    using System;
    using System.Reflection;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using AcceptanceTesting.Customization;
    using EndpointTemplates;
    using Extensibility;
    using NServiceBus.Pipeline;
    using NUnit.Framework;

    public class When_nested_send_with_outer_MSMQ_settings : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_apply_default_reply_in_inner_send()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<SenderEndpoint>(c => c
                    .When(s =>
                    {
                        var sendOptions = new SendOptions();
                        sendOptions.UseDeadLetterQueue(true);
                        sendOptions.UseJournalQueue(true);
                        return s.Send(new OuterMessage(), sendOptions);
                    }))
                .WithEndpoint<ReplyEndpoint>()
                .Done(c => c.OuterMessageReceived && c.InnerMessageReceived)
                .Run();

            Assert.IsTrue(context.OuterDeadLetterSetting);
            Assert.IsTrue(context.OuterJournalingSetting);
            Assert.IsNull(context.InnerJournalingSetting);
            Assert.IsNull(context.InnerDeadLetterSetting);
        }

        class Context : ScenarioContext
        {
            public bool OuterMessageReceived { get; set; }
            public bool InnerMessageReceived { get; set; }
            public bool? OuterJournalingSetting { get; set; }
            public bool? InnerJournalingSetting { get; set; }
            public bool? OuterDeadLetterSetting { get; set; }
            public bool? InnerDeadLetterSetting { get; set; }
        }

        class SenderEndpoint : EndpointConfigurationBuilder
        {
            public SenderEndpoint() => EndpointSetup<DefaultServer>((c, r) =>
            {
                c.ConfigureTransport().Routing().RouteToEndpoint(Assembly.GetExecutingAssembly(), Conventions.EndpointNamingConvention(typeof(ReplyEndpoint)));
                c.Pipeline.Register(new InnerSendBehavior(), "sends an inner message on send operations");
                c.Pipeline.Register(new OutgoingStateCaptureBehavior((Context)r.ScenarioContext), "sends an inner message on send operations");
            });

            class InnerSendBehavior : Behavior<IOutgoingLogicalMessageContext>
            {
                public override async Task Invoke(IOutgoingLogicalMessageContext context, Func<Task> next)
                {
                    await next();

                    if (context.Message.MessageType == typeof(OuterMessage))
                    {
                        await context.Send(new InnerMessage());
                    }
                }
            }

            class OutgoingStateCaptureBehavior : Behavior<IOutgoingPhysicalMessageContext>
            {
                Context testContext;

                public OutgoingStateCaptureBehavior(Context testContext)
                {
                    this.testContext = testContext;
                }

                public override Task Invoke(IOutgoingPhysicalMessageContext context, Func<Task> next)
                {
                    var messageType = context.Extensions.Get<OutgoingLogicalMessage>().MessageType;
                    if (messageType == typeof(OuterMessage))
                    {
                        if (context.GetOperationProperties().TryGet("MSMQ.UseJournalQueue", out bool useJournalQueue))
                        {
                            testContext.OuterJournalingSetting = useJournalQueue;
                        }

                        if (context.GetOperationProperties().TryGet("MSMQ.UseDeadLetterQueue", out bool useDeadletterQueue))
                        {
                            testContext.OuterDeadLetterSetting = useDeadletterQueue;
                        }
                    }

                    if (messageType == typeof(InnerMessage))
                    {
                        if (context.GetOperationProperties().TryGet("MSMQ.UseJournalQueue", out bool useJournalQueue))
                        {
                            testContext.InnerJournalingSetting = useJournalQueue;
                        }

                        if (context.GetOperationProperties().TryGet("MSMQ.UseDeadLetterQueue", out bool useDeadletterQueue))
                        {
                            testContext.InnerDeadLetterSetting = useDeadletterQueue;
                        }
                    }

                    return next();
                }
            }
        }

        class ReplyEndpoint : EndpointConfigurationBuilder
        {
            public ReplyEndpoint() => EndpointSetup<DefaultServer>();

            class MessageHandler : IHandleMessages<OuterMessage>, IHandleMessages<InnerMessage>
            {
                Context testContext;

                public MessageHandler(Context testContext) => this.testContext = testContext;

                public Task Handle(OuterMessage message, IMessageHandlerContext context)
                {
                    testContext.OuterMessageReceived = true;
                    return Task.FromResult(0);
                }

                public Task Handle(InnerMessage message, IMessageHandlerContext context)
                {
                    testContext.InnerMessageReceived = true;
                    return Task.FromResult(0);
                }
            }
        }

        class OuterMessage : IMessage
        {
        }

        class InnerMessage : IMessage
        {
        }
    }
}