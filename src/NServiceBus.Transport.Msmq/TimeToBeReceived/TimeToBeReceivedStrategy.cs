namespace NServiceBus.Transport.Msmq
{
    using Performance.TimeToBeReceived;
    using System.Collections.Generic;
    using System.Messaging;

    abstract class TimeToBeReceivedStrategy
    {
        public virtual StartupCheckResult PerformStartupCheck() => StartupCheckResult.Success;

        public virtual void AssertDispatchOperationSafe(TransportTransaction transportTransaction, UnicastTransportOperation transportOperation, MsmqAddress destinationAddress)
        {
        }

        public virtual void Apply(Message result, DiscardIfNotReceivedBefore timeToBeReceived)
        {
        }

        public virtual bool DiscardDueToElapsedTtbr(Dictionary<string, string> headers) => false;
    }
}