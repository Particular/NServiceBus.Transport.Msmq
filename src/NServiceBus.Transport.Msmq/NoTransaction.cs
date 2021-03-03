namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Messaging;

    class NoTransaction : IMsmqTransaction
    {
        public bool RollbackBeforeErrorHandlingRequired => false;

        public bool SupportsTimeToBeReceived => true;

        public Message Receive(MessageQueue inputQueue, TimeSpan timeout)
        {
            return inputQueue.Receive(timeout, inputQueue.Transactional ? MessageQueueTransactionType.Single : MessageQueueTransactionType.None);
        }

        public void SendMessage(MessageQueue targetQueue, Message message)
        {
            targetQueue.Send(message, targetQueue.Transactional ? MessageQueueTransactionType.Single : MessageQueueTransactionType.None);
        }

        public void Rollback()
        {
            //no-op since the message is already acked on Receive
        }

        public void Commit()
        {
            //no-op since the message is already acked on Receive
        }

        public void Dispose() { }
    }
}