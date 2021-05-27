namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Messaging;
    using System.Threading;
    using System.Threading.Tasks;
    using Extensibility;
    using Logging;
    using Transport;

    abstract class ReceiveStrategy
    {
        public abstract Task ReceiveMessage(CancellationToken cancellationToken = default);

        public void Init(MessageQueue inputQueue, MessageQueue errorQueue, OnMessage onMessage, OnError onError, Action<string, Exception, CancellationToken> criticalError, bool ignoreIncomingTimeToBeReceivedHeaders)
        {
            this.inputQueue = inputQueue;
            this.errorQueue = errorQueue;
            this.onMessage = onMessage;
            this.onError = onError;
            this.criticalError = criticalError;
            this.ignoreIncomingTimeToBeReceivedHeaders = ignoreIncomingTimeToBeReceivedHeaders;
        }

        protected bool TryReceive(MessageQueueTransactionType transactionType, out Message message)
        {
            try
            {
                message = inputQueue.Receive(TimeSpan.FromMilliseconds(10), transactionType);

                return true;
            }
            catch (MessageQueueException ex)
            {
                if (ex.MessageQueueErrorCode == MessageQueueErrorCode.IOTimeout)
                {
                    //We should only get an IOTimeout exception here if another process removed the message between us peeking and now.
                    message = null;
                    return false;
                }
                throw;
            }
        }

        protected bool TryReceive(MessageQueueTransaction transaction, out Message message)
        {
            try
            {
                message = inputQueue.Receive(TimeSpan.FromMilliseconds(10), transaction);

                return true;
            }
            catch (MessageQueueException ex)
            {
                if (ex.MessageQueueErrorCode == MessageQueueErrorCode.IOTimeout)
                {
                    //We should only get an IOTimeout exception here if another process removed the message between us peeking and now.
                    message = null;
                    return false;
                }
                throw;
            }
        }

        protected bool TryExtractHeaders(Message message, out Dictionary<string, string> headers)
        {
            try
            {
                headers = MsmqUtilities.ExtractHeaders(message);
                return true;
            }
            catch (Exception ex)
            {
                var error = $"Message '{message.Id}' has corrupted headers";

                Logger.Warn(error, ex);

                headers = null;
                return false;
            }
        }

        protected void MovePoisonMessageToErrorQueue(Message message, MessageQueueTransaction transaction)
        {
            var error = $"Message '{message.Id}' is classified as a poison message and will be moved to the configured error queue.";

            Logger.Error(error);

            errorQueue.Send(message, transaction);
        }

        protected void MovePoisonMessageToErrorQueue(Message message, MessageQueueTransactionType transactionType)
        {
            var error = $"Message '{message.Id}' is classified as a poison message and will be moved to  the configured error queue.";

            Logger.Error(error);

            errorQueue.Send(message, transactionType);
        }

        protected async Task TryProcessMessage(string messageId, Dictionary<string, string> headers, Stream bodyStream, TransportTransaction transaction, ContextBag context, CancellationToken cancellationToken = default)
        {
            if (!ignoreIncomingTimeToBeReceivedHeaders && TimeToBeReceived.HasElapsed(headers))
            {
                Logger.Debug($"Discarding message {messageId} due to lapsed Time To Be Received header");
                return;
            }

            var body = await ReadStream(bodyStream, cancellationToken).ConfigureAwait(false);
            var messageContext = new MessageContext(messageId, headers, body, transaction, context);

            await onMessage(messageContext, cancellationToken).ConfigureAwait(false);
        }

        protected async Task<ErrorHandleResult> HandleError(Message message, Exception exception, TransportTransaction transportTransaction, int processingAttempts, ContextBag context, CancellationToken cancellationToken = default)
        {
            try
            {
                var body = await ReadStream(message.BodyStream, cancellationToken).ConfigureAwait(false);
                var headers = MsmqUtilities.ExtractHeaders(message);

                var errorContext = new ErrorContext(exception, headers, message.Id, body, transportTransaction, processingAttempts, context);

                return await onError(errorContext, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ex.IsCausedBy(cancellationToken))
            {
                criticalError($"Failed to execute recoverability policy for message with native ID: `{message.Id}`", ex, cancellationToken);

                //best thing we can do is roll the message back if possible
                return ErrorHandleResult.RetryRequired;
            }
        }

        static async Task<byte[]> ReadStream(Stream bodyStream, CancellationToken cancellationToken)
        {
            bodyStream.Seek(0, SeekOrigin.Begin);
            var length = (int)bodyStream.Length;
            var body = new byte[length];
            await bodyStream.ReadAsync(body, 0, length, cancellationToken).ConfigureAwait(false);
            return body;
        }

        protected bool IsQueuesTransactional => errorQueue.Transactional;

        MessageQueue inputQueue;
        MessageQueue errorQueue;
        OnMessage onMessage;
        OnError onError;
        Action<string, Exception, CancellationToken> criticalError;
        bool ignoreIncomingTimeToBeReceivedHeaders;

        static ILog Logger = LogManager.GetLogger<ReceiveStrategy>();
    }
}
