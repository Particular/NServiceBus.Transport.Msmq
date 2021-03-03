namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Messaging;

    class NativeMsmqTransaction : IMsmqTransaction
    {
        public NativeMsmqTransaction(bool enlistSendsInReceiveTransaction, TransportTransaction transportTransaction)
        {
            this.enlistSendsInReceiveTransaction = enlistSendsInReceiveTransaction;

            nativeTransaction = new MessageQueueTransaction();

            if (enlistSendsInReceiveTransaction)
            {
                transportTransaction.Set(nativeTransaction);
            }
        }

        public bool RollbackBeforeErrorHandlingRequired => true;

        public Message Receive(MessageQueue inputQueue, TimeSpan timeout)
        {
            nativeTransaction.Begin();

            return inputQueue.Receive(TimeSpan.FromMilliseconds(10), nativeTransaction);
        }

        public void SendMessage(MessageQueue targetQueue, Message message)
        {
            if (enlistSendsInReceiveTransaction)
            {
                targetQueue.Send(message, nativeTransaction);
            }
            else
            {
                targetQueue.Send(message, targetQueue.Transactional ? MessageQueueTransactionType.Single : MessageQueueTransactionType.None);
            }
        }

        public void Commit()
        {
            nativeTransaction.Commit();
        }

        public void Rollback()
        {
            nativeTransaction.Abort();
        }

        public void Dispose()
        {
            nativeTransaction?.Dispose();
        }

        MessageQueueTransaction nativeTransaction;

        readonly bool enlistSendsInReceiveTransaction;
    }
}