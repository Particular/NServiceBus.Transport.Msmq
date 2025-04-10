﻿namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System.Threading;
    using System.Threading.Tasks;
    using AcceptanceTesting.Customization;
    using AcceptanceTesting;
    using Features;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_distributing_an_event : NServiceBusAcceptanceTest
    {
        static string SubscriberEndpoint => Conventions.EndpointNamingConvention(typeof(Subscriber));

        [Test]
        public async Task Should_round_robin()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<Publisher>(b => b.When(c => c.Subscriptions == 2, async (session, c) =>
                {
                    for (var i = 0; i < 10; i++)
                    {
                        await session.Publish(new MyEvent()).ConfigureAwait(false);
                    }
                }))
                .WithEndpoint<Subscriber1>()
                .WithEndpoint<Subscriber2>()
                .Done(c => c.Receiver1TimesCalled + c.Receiver2TimesCalled >= 10)
                .Run();

            Assert.Multiple(() =>
            {
                Assert.That(context.Receiver1TimesCalled, Is.EqualTo(5));
                Assert.That(context.Receiver2TimesCalled, Is.EqualTo(5));
            });
        }

        public class Context : ScenarioContext
        {
            public int Subscriptions { get; set; }

            int receiver1TimesCalled;
            public int Receiver1TimesCalled => receiver1TimesCalled;
            public void OnReceived1()
            {
                Interlocked.Increment(ref receiver1TimesCalled);
            }

            int receiver2TimesCalled;
            public int Receiver2TimesCalled => receiver2TimesCalled;
            public void OnReceived2()
            {
                Interlocked.Increment(ref receiver2TimesCalled);
            }
        }

        public class Publisher : EndpointConfigurationBuilder
        {
            public Publisher()
            {
                EndpointSetup<DefaultServer>(c =>
                {
                    c.DisableFeature<AutoSubscribe>();
                    c.OnEndpointSubscribed<Context>((s, context) =>
                    {
                        context.Subscriptions++;
                    });
                    c.LimitMessageProcessingConcurrencyTo(1);
                });
            }
        }

        //Used only to generate a name
        public class Subscriber : EndpointConfigurationBuilder
        {
        }

        public class Subscriber1 : EndpointConfigurationBuilder
        {
            public Subscriber1()
            {
                EndpointSetup<DefaultServer>(c =>
                    {
                        c.OverrideLocalAddress(SubscriberEndpoint + "-1");
                    }, p => p.RegisterPublisherFor<MyEvent>(typeof(Publisher)))
                    .CustomEndpointName(SubscriberEndpoint);
            }

            public class MyMessageHandler : IHandleMessages<MyEvent>
            {
                readonly Context scenarioContext;
                public MyMessageHandler(Context scenarioContext)
                {
                    this.scenarioContext = scenarioContext;
                }

                public Task Handle(MyEvent message, IMessageHandlerContext context)
                {
                    scenarioContext.OnReceived1();
                    return Task.FromResult(0);
                }
            }
        }

        public class Subscriber2 : EndpointConfigurationBuilder
        {
            public Subscriber2()
            {
                EndpointSetup<DefaultServer>(c =>
                    {
                        c.OverrideLocalAddress(SubscriberEndpoint + "-2");
                    }, p => p.RegisterPublisherFor<MyEvent>(typeof(Publisher)))
                    .CustomEndpointName(SubscriberEndpoint);
            }

            public class MyMessageHandler : IHandleMessages<MyEvent>
            {
                readonly Context scenarioContext;
                public MyMessageHandler(Context scenarioContext)
                {
                    this.scenarioContext = scenarioContext;
                }

                public Task Handle(MyEvent message, IMessageHandlerContext context)
                {
                    scenarioContext.OnReceived2();
                    return Task.FromResult(0);
                }
            }
        }

        public class MyEvent : IEvent
        {
        }
    }
}