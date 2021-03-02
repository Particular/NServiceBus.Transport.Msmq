namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Collections.Generic;
    using System.Messaging;
    using System.Threading.Tasks;
    using System.Transactions;
    using NServiceBus.Extensibility;
    using Transport;

    class TransactionScopeStrategy : ReceiveStrategy
    {
        public TransactionScopeStrategy(TransactionOptions transactionOptions, MsmqFailureInfoStorage failureInfoStorage)
        {
            this.transactionOptions = transactionOptions;
            this.failureInfoStorage = failureInfoStorage;
        }

        public override async Task<(string, Dictionary<string, string>, bool)> ReceiveMessage(ContextBag context)
        {
            Message message = null;
            Dictionary<string, string> headers = null;
            var onMessageFailed = false;

            try
            {
                using (var scope = new TransactionScope(TransactionScopeOption.RequiresNew, transactionOptions, TransactionScopeAsyncFlowOption.Enabled))
                {
                    if (!TryReceive(MessageQueueTransactionType.Automatic, out message))
                    {
                        return (null, null, true);
                    }

                    if (!TryExtractHeaders(message, out headers))
                    {
                        MovePoisonMessageToErrorQueue(message, MessageQueueTransactionType.Automatic);

                        scope.Complete();
                        return (message.Id, null, true);
                    }

                    var shouldCommit = await ProcessMessage(message, headers, context).ConfigureAwait(false);

                    if (shouldCommit)
                    {
                        scope.Complete();
                        failureInfoStorage.ClearFailureInfoForMessage(message.Id);
                    }
                }
            }
            // We'll only get here if Complete/Dispose throws which should be rare.
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

        async Task<bool> ProcessMessage(Message message, Dictionary<string, string> headers, ContextBag context)
        {
            var transportTransaction = new TransportTransaction();
            transportTransaction.Set(Transaction.Current);

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

        TransactionOptions transactionOptions;
        MsmqFailureInfoStorage failureInfoStorage;
    }
}