namespace NServiceBus.Transport.Msmq.Tests
{
    using System;
    using Transport;
    using NUnit.Framework;

    [TestFixture]
    public class MsmqMessagePumpTests
    {

        [Test]
        public void ShouldThrowIfConfiguredToReceiveFromRemoteQueue()
        {
            var receiveSettings = new ReceiveSettings("test receiver", "queue@remote", false, false, "error");
            var messagePump = new MessagePump(mode => null, TimeSpan.Zero, (_, __, ___) => { }, new MsmqTransport(), receiveSettings);

            var exception = Assert.ThrowsAsync<Exception>(async () =>
            {
                await messagePump.Initialize(new PushRuntimeSettings(), (_, __) => null, (_, __) => null);
            });

            Assert.That(exception.Message, Does.Contain($"MSMQ Dequeuing can only run against the local machine. Invalid inputQueue name '{receiveSettings.ReceiveAddress}'."));
        }
    }
}