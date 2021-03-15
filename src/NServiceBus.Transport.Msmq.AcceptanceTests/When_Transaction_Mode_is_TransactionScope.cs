namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using System.Management;
    using System.Threading.Tasks;
    using AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_Transaction_Mode_is_TransactionScope : NServiceBusAcceptanceTest
    {
        public When_Transaction_Mode_is_TransactionScope()
        {
            var wmiQuery = "SELECT * FROM Win32_Service WHERE Name='MSDTC'";
            var searcher = new ManagementObjectSearcher(wmiQuery);
            var results = searcher.Get();

            foreach (var service in results)
            {
                var mode = (string)service["StartMode"];

                if (mode != "Disabled")
                {
                    Assert.Ignore("This test requires MSDTC service to be set to Disabled.");
                }
            }
        }

        [Test]
        public void Should_throw_if_MSDTC_is_not_active()
        {
            var exception = Assert.ThrowsAsync<Exception>(async () =>
            {
                await Scenario.Define<ScenarioContext>()
                    .WithEndpoint<TransactionalEndpoint>(b => b.When((session, c) => Task.CompletedTask))
                    .Run();
            });

            Assert.AreEqual(
                "Transaction mode is set to `TransactionScope`. This depends on Microsoft Distributed Transaction Coordinator (MSDTC) which is not available. Either enable MSDTC, enable Outbox, or lower the transaction mode to `SendsAtomicWithReceive`.",
                exception.Message
                );
        }

        class TransactionalEndpoint : EndpointConfigurationBuilder
        {
            public TransactionalEndpoint()
            {
                EndpointSetup<DefaultServer>((config, context) =>
                {
                    config.ConfigureTransport().TransportTransactionMode = TransportTransactionMode.TransactionScope;
                });
            }
        }
    }
}
