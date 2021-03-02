namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Collections.Generic;
    using System.Messaging;
    using System.Threading.Tasks;
    using NServiceBus.Extensibility;
    using Transport;

    class NoTransactionStrategy : ReceiveStrategy
    {
        public override async Task<(string, Dictionary<string, string>, bool)> ReceiveMessage(ContextBag context)
        {
            if (!TryReceive(MessageQueueTransactionType.None, out var message))
            {
                return (null, null, true);
            }

            if (!TryExtractHeaders(message, out var headers))
            {
                MovePoisonMessageToErrorQueue(message, IsQueuesTransactional ? MessageQueueTransactionType.Single : MessageQueueTransactionType.None);
                return (message.Id, null, true);
            }

            var transportTransaction = new TransportTransaction();
            var onMessageFailed = false;

            using (var bodyStream = message.BodyStream)
            {
                try
                {
                    await TryProcessMessage(message.Id, headers, bodyStream, transportTransaction, context).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    onMessageFailed = true;
                    message.BodyStream.Position = 0;

                    await HandleError(message, exception, transportTransaction, 1, context).ConfigureAwait(false);
                }
            }

            return (message.Id, headers, onMessageFailed);
        }
    }
}