namespace NServiceBus.Transport.Msmq.Tests
{
    using System;
    using System.Collections.Generic;
    using Transport;
    using NUnit.Framework;

    [TestFixture]
    public class MsmqMessagePumpTests
    {
        [Test]
        public void ShouldThrowIfConfiguredToReceiveFromRemoteQueue()
        {
            var remoteAddress = new QueueAddress("queue", properties: new Dictionary<string, string> { { "machine", "remote" } });
            var receiveSettings = new ReceiveSettings("test receiver", remoteAddress, false, false, "error");
            var messagePump = new MessagePump(mode => null, TimeSpan.Zero, TransportTransactionMode.SendsAtomicWithReceive, false, (_, __, ___) => { }, receiveSettings);

            var exception = Assert.ThrowsAsync<Exception>(async () =>
            {
                await messagePump.Initialize(new PushRuntimeSettings(), (_, __) => null, (_, __) => null);
            });

            Assert.That(exception.Message, Does.Contain($"MSMQ Dequeuing can only run against the local machine. Invalid inputQueue name '{receiveSettings.ReceiveAddress}'."));
        }
    }
}