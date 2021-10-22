namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Messaging;
    using System.Threading.Tasks;
    using System.Transactions;
    using DeliveryConstraints;
    using Extensibility;
    using Performance.TimeToBeReceived;
    using Transport;
    using Unicast.Queuing;

    class MsmqMessageDispatcher : IDispatchMessages
    {
        public MsmqMessageDispatcher(MsmqSettings settings)
        {
            this.settings = settings;
        }

        public Task Dispatch(TransportOperations outgoingMessages, TransportTransaction transaction, ContextBag context)
        {
            Guard.AgainstNull(nameof(outgoingMessages), outgoingMessages);

            if (outgoingMessages.MulticastTransportOperations.Any())
            {
                throw new Exception("The MSMQ transport only supports unicast transport operations.");
            }

            foreach (var unicastTransportOperation in outgoingMessages.UnicastTransportOperations)
            {
                ExecuteTransportOperation(transaction, unicastTransportOperation, context);
            }

            return TaskEx.CompletedTask;
        }

        public void DispatchDelayedMessage(string id, byte[] extension, byte[] body, string destination, TransportTransaction transportTransaction, ContextBag context)
        {
            var headersAndProperties = MsmqUtilities.DeserializeMessageHeaders(extension);

            var request = new OutgoingMessage(id, headersAndProperties, body);

            ExecuteTransportOperation(transportTransaction, new UnicastTransportOperation(request, destination), context);
        }

        void ExecuteTransportOperation(TransportTransaction transaction, UnicastTransportOperation transportOperation, ContextBag context)
        {
            var message = transportOperation.Message;

            var destination = transportOperation.Destination;
            var destinationAddress = MsmqAddress.Parse(destination);

            var deliveryConstraints = transportOperation.DeliveryConstraints;

            if (IsCombiningTimeToBeReceivedWithTransactions(
                transaction,
                transportOperation.RequiredDispatchConsistency,
                transportOperation.DeliveryConstraints))
            {
                if (settings.DisableNativeTtbrInTransactions)
                {
                    deliveryConstraints = deliveryConstraints.Except(deliveryConstraints.OfType<DiscardIfNotReceivedBefore>()).ToList();
                }
                else
                {
                    throw new Exception($"Failed to send message to address: {destinationAddress.Queue}@{destinationAddress.Machine}. Sending messages with a custom TimeToBeReceived is not supported on transactional MSMQ.");
                }
            }

            try
            {
                using (var q = new MessageQueue(destinationAddress.FullPath, false, settings.UseConnectionCache, QueueAccessMode.Send))
                {
                    using (var toSend = MsmqUtilities.Convert(message, deliveryConstraints))
                    {
                        if (context.TryGet<bool>(DeadLetterQueueOptionExtensions.KeyDeadLetterQueue, out var useDeadLetterQueue))
                        {
                            toSend.UseDeadLetterQueue = useDeadLetterQueue;
                        }
                        else
                        {
                            var ttbrRequested = toSend.TimeToBeReceived < MessageQueue.InfiniteTimeout;
                            toSend.UseDeadLetterQueue = ttbrRequested
                                ? settings.UseDeadLetterQueueForMessagesWithTimeToBeReceived
                                : settings.UseDeadLetterQueue;
                        }

                        toSend.UseJournalQueue = context.TryGet<bool>(JournalOptionExtensions.KeyJournaling, out var useJournalQueue)
                            ? useJournalQueue
                            : settings.UseJournalQueue;

                        toSend.TimeToReachQueue = settings.TimeToReachQueue;

                        if (message.Headers.TryGetValue(Headers.ReplyToAddress, out var replyToAddress))
                        {
                            toSend.ResponseQueue = new MessageQueue(MsmqAddress.Parse(replyToAddress).FullPath);
                        }

                        var label = GetLabel(message);

                        if (transportOperation.RequiredDispatchConsistency == DispatchConsistency.Isolated)
                        {
                            q.Send(toSend, label, GetIsolatedTransactionType());
                            return;
                        }

                        if (TryGetNativeTransaction(transaction, out var activeTransaction))
                        {
                            q.Send(toSend, label, activeTransaction);
                            return;
                        }

                        q.Send(toSend, label, GetTransactionTypeForSend());
                    }
                }
            }
            catch (MessageQueueException ex)
            {
                if (ex.MessageQueueErrorCode == MessageQueueErrorCode.QueueNotFound)
                {
                    var msg = destination == null
                        ? "Failed to send message. Target address is null."
                        : $"Failed to send message to address: [{destination}]";

                    throw new QueueNotFoundException(destination, msg, ex);
                }

                ThrowFailedToSendException(destination, ex);
            }
            catch (Exception ex)
            {
                ThrowFailedToSendException(destination, ex);
            }
        }

        bool IsCombiningTimeToBeReceivedWithTransactions(TransportTransaction transaction, DispatchConsistency requiredDispatchConsistency, List<DeliveryConstraint> deliveryConstraints)
        {
            if (!settings.UseTransactionalQueues)
            {
                return false;
            }

            if (requiredDispatchConsistency == DispatchConsistency.Isolated)
            {
                return false;
            }

            var timeToBeReceivedRequested = deliveryConstraints.TryGet(out DiscardIfNotReceivedBefore discardIfNotReceivedBefore) && discardIfNotReceivedBefore.MaxTime < MessageQueue.InfiniteTimeout;

            if (!timeToBeReceivedRequested)
            {
                return false;
            }

            if (Transaction.Current != null)
            {
                return true;
            }


            return TryGetNativeTransaction(transaction, out _);
        }

        static bool TryGetNativeTransaction(TransportTransaction transportTransaction, out MessageQueueTransaction transaction)
        {
            return transportTransaction.TryGet(out transaction);
        }

        MessageQueueTransactionType GetIsolatedTransactionType()
        {
            return settings.UseTransactionalQueues ? MessageQueueTransactionType.Single : MessageQueueTransactionType.None;
        }

        string GetLabel(OutgoingMessage message)
        {
            var messageLabel = settings.LabelGenerator(new ReadOnlyDictionary<string, string>(message.Headers));
            if (messageLabel == null)
            {
                throw new Exception("MSMQ label convention returned a null. Either return a valid value or a String.Empty to indicate 'no value'.");
            }
            if (messageLabel.Length > 240)
            {
                throw new Exception("MSMQ label convention returned a value longer than 240 characters. This is not supported.");
            }
            return messageLabel;
        }

        static void ThrowFailedToSendException(string address, Exception ex)
        {
            if (address == null)
            {
                throw new Exception("Failed to send message.", ex);
            }

            throw new Exception($"Failed to send message to address: {address}", ex);
        }

        MessageQueueTransactionType GetTransactionTypeForSend()
        {
            if (!settings.UseTransactionalQueues)
            {
                return MessageQueueTransactionType.None;
            }

            return Transaction.Current != null
                ? MessageQueueTransactionType.Automatic
                : MessageQueueTransactionType.Single;
        }

        public const string TimeoutDestination = "NServiceBus.Timeout.Destination";
        public const string TimeoutAt = "NServiceBus.Timeout.Expire";

        MsmqSettings settings;
    }
}