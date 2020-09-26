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
            var messagePump = new MessagePump(mode => null, TimeSpan.Zero, false);
            var pushSettings = new PushSettings("queue@remote", "error", false, TransportTransactionMode.None);

            var exception = Assert.ThrowsAsync<Exception>(async () =>
            {
                await messagePump.Init(context => null, context => null, null, pushSettings);
            });

            Assert.That(exception.Message, Does.Contain($"MSMQ Dequeuing can only run against the local machine. Invalid inputQueue name '{pushSettings.InputQueue}'."));
        }
    }
}