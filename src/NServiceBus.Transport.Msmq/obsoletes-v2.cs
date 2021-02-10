#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable 1591

namespace NServiceBus
{
    using System;
    using System.Collections.Generic;
    using System.Transactions;

    public static partial class MsmqConfigurationExtensions
    {
        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.ApplyCustomLabelToOutgoingMessages",
            RemoveInVersion = "3",
            TreatAsErrorFromVersion = "2")]
        public static TransportExtensions<MsmqTransport> ApplyLabelToMessages(
            this TransportExtensions<MsmqTransport> transportExtensions,
            Func<IReadOnlyDictionary<string, string>, string> labelGenerator) => throw new NotImplementedException();

        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.ConfigureTransactionScope",
            RemoveInVersion = "3",
            TreatAsErrorFromVersion = "2")]
        public static TransportExtensions<MsmqTransport> TransactionScopeOptions(
            this TransportExtensions<MsmqTransport> transportExtensions, TimeSpan? timeout = null,
            IsolationLevel? isolationLevel = null) => throw new NotImplementedException();

        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.UseDeadLetterQueueForMessagesWithTimeToBeReceived",
            RemoveInVersion = "3",
            TreatAsErrorFromVersion = "2")]
        public static void UseDeadLetterQueueForMessagesWithTimeToBeReceived(this TransportExtensions<MsmqTransport> config) =>
            throw new NotImplementedException();

        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.ExecuteInstaller",
            RemoveInVersion = "3",
            TreatAsErrorFromVersion = "2")]
        public static void DisableInstaller(this TransportExtensions<MsmqTransport> config) =>
            throw new NotImplementedException();

        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.UseDeadLetterQueue",
            RemoveInVersion = "3",
            TreatAsErrorFromVersion = "2")]
        public static void DisableDeadLetterQueueing(this TransportExtensions<MsmqTransport> config) =>
            throw new NotImplementedException();

        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.UseConnectionCache",
            RemoveInVersion = "3",
            TreatAsErrorFromVersion = "2")]
        public static void DisableConnectionCachingForSends(this TransportExtensions<MsmqTransport> config) =>
            throw new NotImplementedException();

        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.UseTransactionalQueues",
            RemoveInVersion = "3",
            TreatAsErrorFromVersion = "2")]
        public static void UseNonTransactionalQueues(this TransportExtensions<MsmqTransport> config) =>
            throw new NotImplementedException();

        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.UseJournalQueue",
            RemoveInVersion = "3",
            TreatAsErrorFromVersion = "2")]
        public static void EnableJournaling(this TransportExtensions<MsmqTransport> config) =>
            throw new NotImplementedException();

        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.TimeToReachQueue",
            RemoveInVersion = "3",
            TreatAsErrorFromVersion = "2")]
        public static void TimeToReachQueue(this TransportExtensions<MsmqTransport> config, TimeSpan timeToReachQueue) =>
            throw new NotImplementedException();

        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.DisableNativeTtbrInTransactions",
            RemoveInVersion = "3",
            TreatAsErrorFromVersion = "2")]
        public static void DisableNativeTimeToBeReceivedInTransactions(this TransportExtensions<MsmqTransport> config) =>
            throw new NotImplementedException();

        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.IgnoreIncomingTimeToBeReceivedHeaders",
            RemoveInVersion = "3",
            TreatAsErrorFromVersion = "2")]
        public static void IgnoreIncomingTimeToBeReceivedHeaders(this TransportExtensions<MsmqTransport> config) =>
            throw new NotImplementedException();
    }
}
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning restore 1591