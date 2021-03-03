namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Messaging;

    interface IMsmqTransaction : IDisposable
    {
        bool RollbackBeforeErrorHandlingRequired { get; }

        void Commit();

        Message Receive(MessageQueue inputQueue, TimeSpan timeout);

        void Rollback();

        void SendMessage(MessageQueue targetQueue, Message message);
    }
}