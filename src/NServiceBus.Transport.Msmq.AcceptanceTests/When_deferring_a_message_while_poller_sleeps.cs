namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_deferring_a_message_while_poller_sleeps : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_forward_to_error_queue()
        {
            Requires.DelayedDelivery();

            var longDelay = TimeSpan.FromSeconds(120); //Too long for the test to pass
            var shortDelay = TimeSpan.FromSeconds(5);

            var context = await Scenario.Define<Context>()
                .WithEndpoint<Endpoint>(b => b.When((session, c) =>
                {
                    var options = new SendOptions();

                    options.DelayDeliveryWith(longDelay);
                    options.RouteToThisEndpoint();

                    return session.Send(new MyMessage(), options);
                }).When(c => c.LongDelayFetched, session =>
                {
                    var options = new SendOptions();

                    options.DelayDeliveryWith(shortDelay);
                    options.RouteToThisEndpoint();

                    return session.Send(new MyMessage(), options);

                }))
                .Done(c => c.Processed)
                .Run();

            Assert.True(context.Processed);
        }

        public class Context : ScenarioContext
        {
            public bool Processed { get; set; }
            public bool LongDelayFetched { get; set; }
        }

        public class Endpoint : EndpointConfigurationBuilder
        {
            public Endpoint()
            {
                EndpointSetup<DefaultServer>((endpointConfiguration, run) =>
                {
                    var transport = endpointConfiguration.ConfigureTransport<MsmqTransport>();
                    transport.DelayedDelivery = new DelayedDeliverySettings(new TestTimeoutStorage(transport.DelayedDelivery.TimeoutStorage, (Context)run.ScenarioContext));
                });
            }

            public class MyMessageHandler : IHandleMessages<MyMessage>
            {
                public MyMessageHandler(Context testContext)
                {
                    this.testContext = testContext;
                }

                public Task Handle(MyMessage message, IMessageHandlerContext context)
                {
                    testContext.Processed = true;
                    return Task.FromResult(0);
                }

                Context testContext;
            }
        }

        public class MyMessage : IMessage
        {
        }

        class TestTimeoutStorage : ITimeoutStorage
        {
            readonly ITimeoutStorage impl;
            readonly Context context;

            public TestTimeoutStorage(ITimeoutStorage impl, Context context)
            {
                this.impl = impl;
                this.context = context;
            }

            public Task Initialize(string endpointName, CancellationToken cancellationToken) => impl.Initialize(endpointName, cancellationToken);

            public Task<DateTimeOffset?> Next() => impl.Next();

            public Task Store(TimeoutItem entity) => impl.Store(entity);

            public Task<bool> Remove(TimeoutItem entity) => impl.Remove(entity);

            public Task<bool> BumpFailureCount(TimeoutItem timeout) => impl.BumpFailureCount(timeout);

            public async Task<List<TimeoutItem>> FetchDueTimeouts(DateTimeOffset at)
            {
                List<TimeoutItem> result = await impl.FetchDueTimeouts(at);
                if (result.Any())
                {
                    //When we fetch the initial delayed message, we signal
                    context.LongDelayFetched = true;
                }
                return result;
            }
        }
    }
}
