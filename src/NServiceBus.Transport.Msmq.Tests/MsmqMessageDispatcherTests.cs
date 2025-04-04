﻿namespace NServiceBus.Transport.Msmq.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using NServiceBus.Performance.TimeToBeReceived;
    using NUnit.Framework;
    using Particular.Msmq;
    using Routing;
    using Transport;

    [TestFixture]
    public class MsmqMessageDispatcherTests
    {
        [Test]
        public async Task Should_set_label_when_convention_configured()
        {
            var transportSettings = new MsmqTransport { ApplyCustomLabelToOutgoingMessages = _ => "mylabel" };

            var dispatchedMessage = await DispatchMessage("labelTest", transportSettings);

            Assert.That(dispatchedMessage.Label, Is.EqualTo("mylabel"));
        }

        [Test]
        public async Task Should_default_dlq_to_off_for_messages_with_ttbr()
        {
            var dispatchProperties = new DispatchProperties
            {
                DiscardIfNotReceivedBefore = new DiscardIfNotReceivedBefore(TimeSpan.FromMinutes(10))
            };

            var dispatchedMessage = await DispatchMessage("dlqOffForTTBR", dispatchProperties: dispatchProperties);

            Assert.That(dispatchedMessage.UseDeadLetterQueue, Is.False);
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

            Assert.That(dispatchedMessage.UseDeadLetterQueue, Is.True);
        }

        [Test]
        public async Task Should_set_dlq_by_default_for_non_ttbr_messages()
        {
            var dispatchedMessage = await DispatchMessage("dlqOnByDefault");

            Assert.That(dispatchedMessage.UseDeadLetterQueue, Is.True);
        }

        static async Task<Message> DispatchMessage(string queueName, MsmqTransport settings = null, DispatchProperties dispatchProperties = null, CancellationToken cancellationToken = default)
        {
            settings ??= new MsmqTransport();

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

                dispatchProperties ??= [];
                var transportOperation = new TransportOperation(outgoingMessage, new UnicastAddressTag(queueName), dispatchProperties);

                await messageSender.Dispatch(new TransportOperations(transportOperation), new TransportTransaction(), cancellationToken);

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