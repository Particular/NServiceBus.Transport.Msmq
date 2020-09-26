using System.Threading.Tasks;

namespace NServiceBus.Transport.Msmq.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Messaging;
    using DeliveryConstraints;
    using Extensibility;
    using NUnit.Framework;
    using Performance.TimeToBeReceived;
    using Routing;
    using Transport;

    [TestFixture]
    public class MsmqMessageDispatcherTests
    {
        [Test]
        public async Task Should_set_label_when_convention_configured()
        {
            var dispatchedMessage = await DispatchMessage("labelTest", new MsmqSettings(null), messageLabelGenerator: _ => "mylabel");

            Assert.AreEqual("mylabel", dispatchedMessage.Label);
        }

        [Test]
        public async Task Should_default_dlq_to_off_for_messages_with_ttbr()
        {
            var dispatchedMessage = await DispatchMessage("dlqOffForTTBR", deliveryConstraint: new DiscardIfNotReceivedBefore(TimeSpan.FromMinutes(10)));

            Assert.False(dispatchedMessage.UseDeadLetterQueue);
        }

        [Test]
        public async Task Should_allow_optin_for_dlq_on_ttbr_messages()
        {
            var settings = new MsmqSettings(null)
            {
                UseDeadLetterQueueForMessagesWithTimeToBeReceived = true
            };

            var dispatchedMessage = await DispatchMessage("dlqOnForTTBR", settings, new DiscardIfNotReceivedBefore(TimeSpan.FromMinutes(10)));

            Assert.True(dispatchedMessage.UseDeadLetterQueue);
        }

        [Test]
        public async Task Should_set_dlq_by_default_for_non_ttbr_messages()
        {
            var dispatchedMessage = await DispatchMessage("dlqOnByDefault");

            Assert.True(dispatchedMessage.UseDeadLetterQueue);
        }

        static async Task<Message> DispatchMessage(string queueName, MsmqSettings settings = null, DeliveryConstraint deliveryConstraint = null, Func<IReadOnlyDictionary<string, string>, string> messageLabelGenerator = null)
        {
            if (settings == null)
            {
                settings = new MsmqSettings(null);
            }

            if (messageLabelGenerator == null)
            {
                messageLabelGenerator = _ => string.Empty;
            }

            settings.LabelGenerator = messageLabelGenerator;

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
                var deliveryConstraints = new List<DeliveryConstraint>();

                if (deliveryConstraint != null)
                {
                    deliveryConstraints.Add(deliveryConstraint);
                }

                var transportOperation = new TransportOperation(outgoingMessage, new UnicastAddressTag(queueName), DispatchConsistency.Default, deliveryConstraints);

                await messageSender.Dispatch(new TransportOperations(transportOperation), new TransportTransaction(), new ContextBag());

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