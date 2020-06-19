namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using System.Management;
    using AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_Transaction_Mode_is_TransactionScope : NServiceBusAcceptanceTest
    {
        public When_Transaction_Mode_is_TransactionScope()
        {
            string wmiQuery = "SELECT * FROM Win32_Service WHERE Name='MSDTC'";
            var searcher = new ManagementObjectSearcher(wmiQuery);
            var results = searcher.Get();

            foreach (ManagementObject service in results)
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
                var context = await Scenario.Define<ScenarioContext>()
                        .WithEndpoint<TransactionalEndpoint>(b => b.When((session, c) =>
                        {
                            return TaskEx.CompletedTask;
                        }))
                        .Run();
            });

            Assert.AreEqual(
                "Transaction mode is set to `TransactionScope`. This depends on Microsoft Distributed Transaction Coordinator (MSDTC) which is not available. Either enable MSDTC, or enabled Outbox, or lower transaction mode to `SendsAtomicWithReceive`.",
                exception.Message
                );
        }

        public class TransactionalEndpoint : EndpointConfigurationBuilder
        {
            public TransactionalEndpoint()
            {
                EndpointSetup<DefaultServer>((config, context) =>
                {
                    config.UseTransport<MsmqTransport>()
                            .Transactions(TransportTransactionMode.TransactionScope);
                });
            }
        }
    }
}