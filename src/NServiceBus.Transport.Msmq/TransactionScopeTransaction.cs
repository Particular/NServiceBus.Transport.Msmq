namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Messaging;
    using System.Transactions;

    class TransactionScopeTransaction : IMsmqTransaction
    {
        public TransactionScopeTransaction(TransportTransaction transportTransaction, TransactionOptions transactionOptions)
        {
            transactionScope = new TransactionScope(TransactionScopeOption.RequiresNew, transactionOptions, TransactionScopeAsyncFlowOption.Enabled);
            transportTransaction.Set(transactionScope);
        }

        public bool RollbackBeforeErrorHandlingRequired => true;

        public Message Receive(MessageQueue inputQueue, TimeSpan timeout)
        {
            return inputQueue.Receive(TimeSpan.FromMilliseconds(10), MessageQueueTransactionType.Automatic);
        }

        public void SendMessage(MessageQueue targetQueue, Message message)
        {
            targetQueue.Send(message, MessageQueueTransactionType.Automatic);
        }

        public void Commit()
        {
            transactionScope.Complete();
        }

        public void Rollback()
        {
            //no-op since not calling Complete will automaticallu cause a rollback on Dispose
        }

        public void Dispose()
        {
            transactionScope?.Dispose();
        }

        TransactionScope transactionScope;
    }
}