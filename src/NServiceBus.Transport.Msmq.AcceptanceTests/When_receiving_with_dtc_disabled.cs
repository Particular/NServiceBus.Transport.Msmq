namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using System.Threading.Tasks;
    using System.Transactions;
    using AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_receiving_with_dtc_disabled : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_not_escalate_a_single_durable_rm_to_dtc_tx()
        {
            var context = await Scenario.Define<Context>()
                    .WithEndpoint<NonDTCEndpoint>(b => b.When(session => session.SendLocal(new MyMessage())))
                    .Done(c => c.HandlerInvoked)
                    .Run();

            Assert.AreEqual(Guid.Empty, context.DistributedIdentifierBefore, "No DTC tx should exist before enlistment");
            Assert.That(context.CanEnlistPromotable, Is.True, "A promotable RM should be able to enlist");
        }

        public class Context : ScenarioContext
        {
            public bool HandlerInvoked { get; set; }

            public Guid DistributedIdentifierBefore { get; set; }

            public bool CanEnlistPromotable { get; set; }
        }

        public class NonDTCEndpoint : EndpointConfigurationBuilder
        {
            public NonDTCEndpoint()
            {
                EndpointSetup<DefaultServer>((config, context) =>
                {
                    config.ConfigureTransport().TransportTransactionMode = TransportTransactionMode.SendsAtomicWithReceive;
                });
            }

            public class MyMessageHandler : IHandleMessages<MyMessage>
            {
                readonly Context scenarioContext;
                public MyMessageHandler(Context scenarioContext)
                {
                    this.scenarioContext = scenarioContext;
                }

                public Task Handle(MyMessage messageThatIsEnlisted, IMessageHandlerContext context)
                {
                    using (var tx = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
                    {
                        scenarioContext.DistributedIdentifierBefore = Transaction.Current.TransactionInformation.DistributedIdentifier;

                        scenarioContext.CanEnlistPromotable = Transaction.Current.EnlistPromotableSinglePhase(new FakePromotableResourceManager());

                        tx.Complete();
                    }

                    scenarioContext.HandlerInvoked = true;

                    return Task.FromResult(0);
                }
            }
        }

        public class MyMessage : ICommand
        {
        }
    }
}