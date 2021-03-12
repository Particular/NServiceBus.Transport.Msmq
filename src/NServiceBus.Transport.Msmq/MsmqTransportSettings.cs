namespace NServiceBus
{
    using System;
    using System.Collections.Generic;
    using System.Reflection.Emit;
    using System.Transactions;

    /// <summary>
    /// Provides configuration settings for <see cref="MsmqTransport"/>.
    /// </summary>
    public class MsmqTransportSettings : TransportSettings<MsmqTransport>
    {
        internal MsmqTransportSettings(MsmqTransport transport, RoutingSettings<MsmqTransport> routing)
            : base(transport, routing)
        {
        }

        /// <summary>
        /// Set a delegate to use for applying the <see cref="Label" /> property when sending a message.
        /// </summary>
        /// <remarks>
        /// This delegate will be used for all valid messages sent via MSMQ.
        /// This includes, not just standard messages, but also Audits, Errors and all control messages.
        /// In some cases it may be useful to use the <see cref="Headers.ControlMessageHeader" /> key to determine if a message is
        /// a control message.
        /// The only exception to this rule is received messages with corrupted headers. These messages will be forwarded to the
        /// error queue with no label applied.
        /// </remarks>
        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.ApplyCustomLabelToOutgoingMessages",
            RemoveInVersion = "4",
            TreatAsErrorFromVersion = "3")]
        public MsmqTransportSettings ApplyLabelToMessages(
            Func<IReadOnlyDictionary<string, string>, string> labelGenerator)
        {
            Transport.ApplyCustomLabelToOutgoingMessages = labelGenerator;
            return this;
        }

        /// <summary>
        /// Allows to change the transaction isolation level and timeout for the `TransactionScope` used to receive messages.
        /// </summary>
        /// <remarks>
        /// If not specified the default transaction timeout of the machine will be used and the isolation level will be set to
        /// <see cref="IsolationLevel.ReadCommitted"/>.
        /// </remarks>
        /// <param name="timeout">Transaction timeout duration.</param>
        /// <param name="isolationLevel">Transaction isolation level.</param>
        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.ConfigureTransactionScope",
            RemoveInVersion = "4",
            TreatAsErrorFromVersion = "3")]
        public MsmqTransportSettings TransactionScopeOptions(TimeSpan? timeout = null,
            IsolationLevel? isolationLevel = null)
        {
            Transport.ConfigureTransactionScope(timeout, isolationLevel);
            return this;
        }

        /// <summary>
        /// Moves messages that have exceeded their TimeToBeReceived to the dead letter queue instead of discarding them.
        /// </summary>
        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.UseDeadLetterQueueForMessagesWithTimeToBeReceived",
            RemoveInVersion = "4",
            TreatAsErrorFromVersion = "3")]
        public MsmqTransportSettings UseDeadLetterQueueForMessagesWithTimeToBeReceived()
        {
            Transport.UseDeadLetterQueueForMessagesWithTimeToBeReceived = true;
            return this;
        }

        /// <summary>
        /// Disables the automatic queue creation when installers are enabled using `EndpointConfiguration.EnableInstallers()`.
        /// </summary>
        /// <remarks>
        /// With installers enabled, required queues will be created automatically at startup.While this may be convenient for development,
        /// we instead recommend that queues are created as part of deployment using the CreateQueues.ps1 script included in the NuGet package.
        /// The installers might still need to be enabled to fulfill the installation needs of other components, but this method allows
        /// scripts to be used for queue creation instead.
        /// </remarks>
        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.CreateQueues",
            RemoveInVersion = "4",
            TreatAsErrorFromVersion = "3")]
        public MsmqTransportSettings DisableInstaller()
        {
            Transport.CreateQueues = false;
            return this;
        }

        /// <summary>
        /// This setting should be used with caution. It disables the storing of undeliverable messages
        /// in the dead letter queue. Therefore this setting must only be used where loss of messages 
        /// is an acceptable scenario. 
        /// </summary>
        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.UseDeadLetterQueue",
            RemoveInVersion = "4",
            TreatAsErrorFromVersion = "3")]
        public MsmqTransportSettings DisableDeadLetterQueueing()
        {
            Transport.UseDeadLetterQueue = false;
            return this;
        }

        /// <summary>
        /// Instructs MSMQ to cache connections to a remote queue and re-use them
        /// as needed instead of creating new connections for each message. 
        /// Turning connection caching off will negatively impact the message throughput in 
        /// most scenarios.
        /// </summary>
        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.UseConnectionCache",
            RemoveInVersion = "4",
            TreatAsErrorFromVersion = "3")]
        public MsmqTransportSettings DisableConnectionCachingForSends()
        {
            Transport.UseConnectionCache = false;
            return this;
        }

        /// <summary>
        /// This setting should be used with caution. As the queues are not transactional, any message that has
        /// an exception during processing will not be rolled back to the queue. Therefore this setting must only
        /// be used where loss of messages is an acceptable scenario.  
        /// </summary>
        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.UseTransactionalQueues",
            RemoveInVersion = "4",
            TreatAsErrorFromVersion = "3")]
        public MsmqTransportSettings UseNonTransactionalQueues()
        {
            Transport.UseTransactionalQueues = false;
            return this;
        }

        /// <summary>
        /// Enables the use of journaling messages. Stores a copy of every message received in the journal queue. 
        /// Should be used ONLY when debugging as it can 
        /// potentially use up the MSMQ journal storage quota based on the message volume.
        /// </summary>
        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.UseJournalQueue",
            RemoveInVersion = "4",
            TreatAsErrorFromVersion = "3")]
        public MsmqTransportSettings EnableJournaling()
        {
            Transport.UseJournalQueue = true;
            return this;
        }

        /// <summary>
        /// Overrides the Time-To-Reach-Queue (TTRQ) timespan. The default value if not set is Message.InfiniteTimeout
        /// </summary>
        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.TimeToReachQueue",
            RemoveInVersion = "4",
            TreatAsErrorFromVersion = "3")]
        public MsmqTransportSettings TimeToReachQueue(TimeSpan timeToReachQueue)
        {
            Transport.TimeToReachQueue = timeToReachQueue;
            return this;
        }

        /// <summary>
        /// Disables native Time-To-Be-Received (TTBR) when combined with transactions.
        /// </summary>
        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.UseNonNativeTimeToBeReceivedInTransactions",
            RemoveInVersion = "4",
            TreatAsErrorFromVersion = "3")]
        public MsmqTransportSettings DisableNativeTimeToBeReceivedInTransactions()
        {
            Transport.UseNonNativeTimeToBeReceivedInTransactions = true;
            return this;
        }

        /// <summary>
        /// Ignore incoming Time-To-Be-Received (TTBR) headers. By default an expired TTBR header will result in the message to be discarded.
        /// </summary>
        [ObsoleteEx(
            ReplacementTypeOrMember = "MsmqTransport.IgnoreIncomingTimeToBeReceivedHeaders",
            RemoveInVersion = "4",
            TreatAsErrorFromVersion = "3")]
        public MsmqTransportSettings IgnoreIncomingTimeToBeReceivedHeaders()
        {
            Transport.IgnoreIncomingTimeToBeReceivedHeaders = true;
            return this;
        }
    }
}