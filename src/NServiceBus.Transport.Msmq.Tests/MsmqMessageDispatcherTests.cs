namespace NServiceBus.Transport.Msmq.Tests
{
    using System.Threading.Tasks;
    using System;
    using System.Collections.Generic;
    using System.Messaging;
    using System.Threading;
    using NServiceBus.Performance.TimeToBeReceived;
    using Routing;
    using Transport;
    using NUnit.Framework;

    [TestFixture]
    public class MsmqMessageDispatcherTests
    {
        [Test]
        public async Task Should_set_label_when_convention_configured()
        {
            var transportSettings = new MsmqTransport { ApplyCustomLabelToOutgoingMessages = _ => "mylabel" };

            var dispatchedMessage = await DispatchMessage("labelTest", transportSettings);

            Assert.AreEqual("mylabel", dispatchedMessage.Label);
        }

        [Test]
        public async Task Should_default_dlq_to_off_for_messages_with_ttbr()
        {
            var dispatchProperties = new DispatchProperties
            {
                DiscardIfNotReceivedBefore = new DiscardIfNotReceivedBefore(TimeSpan.FromMinutes(10))
            };

            var dispatchedMessage = await DispatchMessage("dlqOffForTTBR", dispatchProperties: dispatchProperties);

            Assert.False(dispatchedMessage.UseDeadLetterQueue);
        }

        [Test]
        public async Task Should_allow_optin_for_dlq_on_ttbr_messages()
        {

            var transportSettings = new MsmqTransport { UseDeadLetterQueueForMessagesWithTimeToBeReceived = true };
            var dispatchProperties = new DispatchProperties
            {
                DiscardIfNotReceivedBefore = new DiscardIfNotReceivedBefore(TimeSpan.FromMinutes(10))
            };

            var dispatchedMessage = await DispatchMessage("dlqOnForTTBR", transportSettings, dispatchProperties);

            Assert.True(dispatchedMessage.UseDeadLetterQueue);
        }

        [Test]
        public async Task Should_set_dlq_by_default_for_non_ttbr_messages()
        {
            var dispatchedMessage = await DispatchMessage("dlqOnByDefault");

            Assert.True(dispatchedMessage.UseDeadLetterQueue);
        }

        static async Task<Message> DispatchMessage(string queueName, MsmqTransport settings = null, DispatchProperties dispatchProperties = null)
        {
            if (settings == null)
            {
                settings = new MsmqTransport();
            }

            var path = $@".\private$\{queueName}";

            try
            {
                MsmqHelpers.DeleteQueue(path);
                MsmqHelpers.CreateQueue(path);

                var messageSender = new MsmqMessageDispatcher(settings);

                var bytes = new byte[]
                {
                    1
                };
                var headers = new Dictionary<string, string>();
                var outgoingMessage = new OutgoingMessage("1", headers, bytes);

                dispatchProperties = dispatchProperties ?? new DispatchProperties();
                var transportOperation = new TransportOperation(outgoingMessage, new UnicastAddressTag(queueName), dispatchProperties);

                await messageSender.Dispatch(new TransportOperations(transportOperation), new TransportTransaction(), CancellationToken.None);

                using (var queue = new MessageQueue(path))
                using (var message = queue.Receive(TimeSpan.FromSeconds(5)))
                {
                    return message;
                }
            }
            finally
            {
                MsmqHelpers.DeleteQueue(path);
            }
        }
    }
}