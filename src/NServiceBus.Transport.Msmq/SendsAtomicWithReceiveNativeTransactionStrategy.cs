namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Collections.Generic;
    using System.Messaging;
    using System.Threading.Tasks;
    using NServiceBus.Extensibility;
    using Transport;

    class SendsAtomicWithReceiveNativeTransactionStrategy : ReceiveStrategy
    {
        public SendsAtomicWithReceiveNativeTransactionStrategy(MsmqFailureInfoStorage failureInfoStorage)
        {
            this.failureInfoStorage = failureInfoStorage;
        }

        public override async Task<(string, Dictionary<string, string>, bool)> ReceiveMessage(ContextBag context)
        {
            Message message = null;
            Dictionary<string, string> headers = null;
            var onMessageFailed = false;

            try
            {
                using (var msmqTransaction = new MessageQueueTransaction())
                {
                    msmqTransaction.Begin();

                    if (!TryReceive(msmqTransaction, out message))
                    {
                        return (null, null, true);
                    }

                    if (!TryExtractHeaders(message, out headers))
                    {
                        MovePoisonMessageToErrorQueue(message, msmqTransaction);

                        msmqTransaction.Commit();
                        return (message.Id, null, true);
                    }

                    var shouldCommit = await ProcessMessage(msmqTransaction, message, headers, context).ConfigureAwait(false);

                    if (shouldCommit)
                    {
                        msmqTransaction.Commit();
                        failureInfoStorage.ClearFailureInfoForMessage(message.Id);
                    }
                    else
                    {
                        msmqTransaction.Abort();
                    }
                }
            }
            // We'll only get here if Commit/Abort/Dispose throws which should be rare.
            // Note: If that happens the attempts counter will be inconsistent since the message might be picked up again before we can register the failure in the LRU cache.
            catch (Exception exception)
            {
                onMessageFailed = true;
                if (message == null)
                {
                    throw;
                }

                failureInfoStorage.RecordFailureInfoForMessage(message.Id, exception, context);
            }

            return (message.Id, headers, onMessageFailed);
        }

        async Task<bool> ProcessMessage(MessageQueueTransaction msmqTransaction, Message message, Dictionary<string, string> headers, ContextBag context)
        {
            var transportTransaction = new TransportTransaction();

            transportTransaction.Set(msmqTransaction);

            if (failureInfoStorage.TryGetFailureInfoForMessage(message.Id, out var failureInfo))
            {
                var errorHandleResult = await HandleError(message, failureInfo.Exception, transportTransaction, failureInfo.NumberOfProcessingAttempts, context).ConfigureAwait(false);

                if (errorHandleResult == ErrorHandleResult.Handled)
                {
                    return true;
                }
            }

            try
            {
                using (var bodyStream = message.BodyStream)
                {
                    await TryProcessMessage(message.Id, headers, bodyStream, transportTransaction, context).ConfigureAwait(false);
                }
                return true;
            }
            catch (Exception exception)
            {
                failureInfoStorage.RecordFailureInfoForMessage(message.Id, exception, context);

                return false;
            }
        }

        MsmqFailureInfoStorage failureInfoStorage;
    }
}