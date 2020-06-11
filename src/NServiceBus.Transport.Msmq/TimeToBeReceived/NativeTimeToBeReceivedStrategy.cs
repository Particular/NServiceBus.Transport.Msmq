namespace NServiceBus.Transport.Msmq
{
    using DeliveryConstraints;
    using Performance.TimeToBeReceived;
    using System;
    using System.Collections.Generic;
    using System.Messaging;
    using System.Transactions;

    class NativeTimeToBeReceivedStrategy : TimeToBeReceivedStrategy
    {
        readonly bool isTransactional;
        readonly bool outBoxRunning;
        readonly bool auditTTBROverridden;

        public NativeTimeToBeReceivedStrategy(bool isTransactional, bool outBoxRunning, bool auditTTBROverridden)
        {
            this.isTransactional = isTransactional;
            this.outBoxRunning = outBoxRunning;
            this.auditTTBROverridden = auditTTBROverridden;
        }

        public override StartupCheckResult PerformStartupCheck()
        {
            if (!isTransactional)
            {
                return StartupCheckResult.Success;
            }

            if (outBoxRunning)
            {
                return StartupCheckResult.Success;
            }

            if (auditTTBROverridden)
            {
                return StartupCheckResult.Failed("Setting a custom OverrideTimeToBeReceived for audits is not supported on transactional MSMQ.");
            }

            return StartupCheckResult.Success;
        }

        public override void Apply(Message result, DiscardIfNotReceivedBefore timeToBeReceived)
        {
            result.TimeToBeReceived = timeToBeReceived.MaxTime;
        }

        public override void AssertDispatchOperationSafe(TransportTransaction transportTransaction, UnicastTransportOperation transportOperation, MsmqAddress destinationAddress)
        {
            if (IsCombiningTimeToBeReceivedWithTransactions(
                transportTransaction,
                transportOperation.RequiredDispatchConsistency, 
                transportOperation.DeliveryConstraints))
            {
                throw new Exception($"Failed to send message to address: {destinationAddress.Queue}@{destinationAddress.Machine}. Sending messages with a custom TimeToBeReceived is not supported on transactional MSMQ.");
            }
        }

        bool IsCombiningTimeToBeReceivedWithTransactions(TransportTransaction transaction, DispatchConsistency requiredDispatchConsistency, List<DeliveryConstraint> deliveryConstraints)
        {
            if (!isTransactional)
            {
                return false;
            }

            if (requiredDispatchConsistency == DispatchConsistency.Isolated)
            {
                return false;
            }

            var timeToBeReceivedRequested = deliveryConstraints.TryGet(out DiscardIfNotReceivedBefore discardIfNotReceivedBefore) && discardIfNotReceivedBefore.MaxTime < MessageQueue.InfiniteTimeout;

            if (!timeToBeReceivedRequested)
            {
                return false;
            }

            if (Transaction.Current != null)
            {
                return true;
            }

            return TryGetNativeTransaction(transaction, out _);
        }

        static bool TryGetNativeTransaction(TransportTransaction transportTransaction, out MessageQueueTransaction transaction)
        {
            return transportTransaction.TryGet(out transaction);
        }
    }
}