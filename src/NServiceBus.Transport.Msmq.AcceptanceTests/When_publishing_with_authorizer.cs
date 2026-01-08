namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using Configuration.AdvancedExtensibility;
    using Features;
    using Pipeline;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_publishing_with_authorizer : NServiceBusAcceptanceTest
    {

        [Test]
        public async Task Should_only_deliver_to_authorized()
        {
            var context = await Scenario.Define<TestContext>()
                .WithEndpoint<Publisher>(b =>
                    b.When(c => c.Subscriber1Subscribed && c.Subscriber2Subscribed, (session, c) => session.Publish(new MyEvent()))
                )
                .WithEndpoint<Subscriber1>(b => b.When(async (session, c) =>
                {
                    await session.Subscribe<MyEvent>();
                }))
                .WithEndpoint<Subscriber2>(b => b.When(async (session, c) =>
                {
                    await session.Subscribe<MyEvent>();
                }))
                .Done(c =>
                    c.Subscriber1GotTheEvent &&
                    c.DeclinedSubscriber2)
                .Run();

            Assert.Multiple(() =>
            {
                Assert.That(context.Subscriber1GotTheEvent, Is.True);
                Assert.That(context.Subscriber2GotTheEvent, Is.False);
            });
        }

        public class TestContext : ScenarioContext
        {
            public bool Subscriber1GotTheEvent { get; set; }
            public bool Subscriber2GotTheEvent { get; set; }
            public bool Subscriber1Subscribed { get; set; }
            public bool Subscriber2Subscribed { get; set; }
            public bool DeclinedSubscriber2 { get; set; }

            public void MaybeMarkAsCompleted() => MarkAsCompleted(Subscriber1GotTheEvent, Subscriber2GotTheEvent);
        }

        class Publisher : EndpointConfigurationBuilder
        {
            public Publisher()
            {
                EndpointSetup<DefaultServer>(b =>
                {
                    var routingSettings = new RoutingSettings<MsmqTransport>(b.GetSettings());
                    routingSettings.SubscriptionAuthorizer(Authorizer);
                    b.OnEndpointSubscribed<TestContext>((s, context) =>
                    {
                        if (s.SubscriberReturnAddress.Contains("Subscriber1"))
                        {
                            context.Subscriber1Subscribed = true;
                        }

                        if (s.SubscriberReturnAddress.Contains("Subscriber2"))
                        {
                            context.Subscriber2Subscribed = true;
                        }
                    });
                    b.DisableFeature<AutoSubscribe>();
                });
            }

            bool Authorizer(IIncomingPhysicalMessageContext context)
            {
                var isFromSubscriber1 = context
                    .MessageHeaders["NServiceBus.SubscriberEndpoint"]
                    .EndsWith("Subscriber1");
                if (!isFromSubscriber1)
                {
                    var testContext = (TestContext)ScenarioContext;
                    testContext.DeclinedSubscriber2 = true;
                }
                return isFromSubscriber1;
            }
        }

        public class Subscriber1 : EndpointConfigurationBuilder
        {
            public Subscriber1()
            {
                EndpointSetup<DefaultServer>(c =>
                {
                    c.DisableFeature<AutoSubscribe>();
                }, p => p.RegisterPublisherFor<MyEvent>(typeof(Publisher)));
            }

            public class MyHandler : IHandleMessages<MyEvent>
            {
                TestContext context;

                public MyHandler(TestContext context)
                {
                    this.context = context;
                }

                public Task Handle(MyEvent message, IMessageHandlerContext handlerContext)
                {
                    context.Subscriber1GotTheEvent = true;
                    context.MaybeMarkAsCompleted();
                    return Task.CompletedTask;
                }
            }
        }

        public class Subscriber2 : EndpointConfigurationBuilder
        {
            public Subscriber2()
            {
                EndpointSetup<DefaultServer>(c =>
                {
                    c.DisableFeature<AutoSubscribe>();
                }, p => p.RegisterPublisherFor<MyEvent>(typeof(Publisher)));
            }

            public class MyHandler : IHandleMessages<MyEvent>
            {
                TestContext context;

                public MyHandler(TestContext context)
                {
                    this.context = context;
                }

                public Task Handle(MyEvent messageThatIsEnlisted, IMessageHandlerContext handlerContext)
                {
                    context.Subscriber2GotTheEvent = true;
                    context.MaybeMarkAsCompleted();
                    return Task.CompletedTask;
                }
            }
        }

        public class MyEvent : IEvent
        {
        }
    }
}