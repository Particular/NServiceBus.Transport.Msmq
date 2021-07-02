namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Messaging;
    using System.Threading;
    using System.Threading.Tasks;
    using NServiceBus.Extensibility;
    using Transport;

    class NoTransactionStrategy : ReceiveStrategy
    {
        public override async Task ReceiveMessage(CancellationToken cancellationToken = default)
        {
            if (!TryReceive(MessageQueueTransactionType.None, out var message))
            {
                return;
            }

            if (!TryExtractHeaders(message, out var headers))
            {
                MovePoisonMessageToErrorQueue(message, IsQueuesTransactional ? MessageQueueTransactionType.Single : MessageQueueTransactionType.None);
                return;
            }


            var transportTransaction = new TransportTransaction();
            var context = new ContextBag();

            context.Set(message);

            using (var bodyStream = message.BodyStream)
            {
                try
                {
                    await TryProcessMessage(message.Id, headers, bodyStream, transportTransaction, context, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!ex.IsCausedBy(cancellationToken))
                {
                    message.BodyStream.Position = 0;

                    await HandleError(message, ex, transportTransaction, 1, context, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}