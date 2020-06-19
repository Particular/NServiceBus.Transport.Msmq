namespace NServiceBus.Persistence.Msmq
{
    using System.Collections.Generic;

    interface IMsmqSubscriptionStorageQueue
    {
        IEnumerable<MsmqSubscriptionMessage> GetAllMessages();
        void Send(string body, string label);
        void TryReceiveById(string messageId);
    }
}