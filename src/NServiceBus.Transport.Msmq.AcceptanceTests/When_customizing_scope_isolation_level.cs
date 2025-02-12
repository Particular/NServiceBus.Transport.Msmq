﻿namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using System.Threading.Tasks;
    using System.Transactions;
    using AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_customizing_scope_isolation_level : NServiceBusAcceptanceTest
    {
        [Test]
        [TestCase(IsolationLevel.Chaos)]
        [TestCase(IsolationLevel.ReadCommitted)]
        [TestCase(IsolationLevel.ReadUncommitted)]
        [TestCase(IsolationLevel.RepeatableRead)]
        [TestCase(IsolationLevel.Serializable)]
        public async Task Should_honor_configured_level(IsolationLevel isolationLevel)
        {
            var context = await Scenario.Define<Context>()
                    .WithEndpoint<ScopeEndpoint>(g =>
                    {
                        g.CustomConfig(c =>
                        {
                            var transportSettings = (MsmqTransport)c.ConfigureTransport();
                            transportSettings.TransportTransactionMode = TransportTransactionMode.TransactionScope;
                            transportSettings.ConfigureTransactionScope(isolationLevel: isolationLevel);
                        });
                        g.When(b => b.SendLocal(new MyMessage()));
                    })
                    .Done(c => c.Done)
                    .Run();

            Assert.Multiple(() =>
            {
                Assert.That(context.AmbientTransactionPresent, Is.True, "There should be a ambient transaction present");
                Assert.That(isolationLevel, Is.EqualTo(context.IsolationLevel), "There should be an ambient transaction present");
            });
        }

        [Test]
        public void Should_fail_for_snapshot()
        {
            var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await Scenario.Define<Context>()
                        .WithEndpoint<ScopeEndpoint>(g =>
                        {
                            g.CustomConfig(c =>
                            {
                                var transportSettings = (MsmqTransport)c.ConfigureTransport();
                                transportSettings.TransportTransactionMode = TransportTransactionMode.TransactionScope;
                                transportSettings.ConfigureTransactionScope(isolationLevel: IsolationLevel.Snapshot);
                            });
                            g.When(b => b.SendLocal(new MyMessage()));
                        })
                        .Done(c => c.Done)
                        .Run();
            });

            Assert.That(ex.Message, Does.Contain("Isolation level `Snapshot` is not supported by the transport. Consider not sharing the transaction between transport and persistence if persistence should use `IsolationLevel.Snapshot` by using `TransportTransactionMode.SendsAtomicWithReceive` or lower."));
        }
        public class Context : ScenarioContext
        {
            public bool Done { get; set; }
            public bool AmbientTransactionPresent { get; set; }
            public IsolationLevel IsolationLevel { get; set; }
        }

        public class ScopeEndpoint : EndpointConfigurationBuilder
        {
            public ScopeEndpoint()
            {
                EndpointSetup<DefaultServer>();
            }

            class MyMessageHandler : IHandleMessages<MyMessage>
            {
                readonly Context scenarioContext;
                public MyMessageHandler(Context scenarioContext)
                {
                    this.scenarioContext = scenarioContext;
                }

                public Task Handle(MyMessage message, IMessageHandlerContext context)
                {
                    if (Transaction.Current != null)
                    {
                        scenarioContext.AmbientTransactionPresent = Transaction.Current != null;
                        scenarioContext.IsolationLevel = Transaction.Current.IsolationLevel;
                    }
                    scenarioContext.Done = true;

                    return Task.FromResult(0);
                }
            }
        }

        public class MyMessage : IMessage
        { }
    }
}
