namespace NServiceBus
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Messaging;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Transactions;
    using Features;
    using Routing;
    using Support;
    using Transport;
    using Transport.Msmq;

    /// <summary>
    /// Transport definition for MSMQ.
    /// </summary>
    public class MsmqTransport : TransportDefinition, IMessageDrivenSubscriptionTransport
    {
        const string TimeoutQueueSuffix = ".timeouts";

        /// <summary>
        /// Creates a new instance of <see cref="MsmqTransport"/> for configuration.
        /// </summary>
        public MsmqTransport() : base(TransportTransactionMode.TransactionScope, true, false, true)
        {
        }

        /// <inheritdoc />
        public override async Task<TransportInfrastructure> Initialize(HostSettings hostSettings, ReceiveSettings[] receivers, string[] sendingAddresses, CancellationToken cancellationToken = default)
        {
            Guard.AgainstNull(nameof(hostSettings), hostSettings);
            Guard.AgainstNull(nameof(receivers), receivers);
            Guard.AgainstNull(nameof(sendingAddresses), sendingAddresses);

            CheckMachineNameForCompliance.Check();
            ValidateIfDtcIsAvailable();

            // TODO: what to do with send only endpoints
            var useTimeouts = TimeoutStorage != null;
            if (useTimeouts && TimeoutQueueAddress.IsEmpty())
            {
                var mainReceiver = receivers.SingleOrDefault(x => x.Id == "Main");
                if (mainReceiver == null)
                {
                    throw new Exception("No main receiver was found, please specify a timeout queue address when enabling timeouts.");
                }

                var mainQueueAddress = MsmqAddress.Parse(mainReceiver.ReceiveAddress);
                TimeoutQueueAddress = new MsmqAddress(mainQueueAddress.Queue + TimeoutQueueSuffix, mainQueueAddress.Machine);
            }

            if (hostSettings.CoreSettings != null)
            {
                // enforce an explicitly configured error queue when using MSMQ transport with NServiceBus
                if (receivers.Length > 0 && !hostSettings.CoreSettings.TryGetExplicitlyConfiguredErrorQueueAddress(out _))
                {
                    throw new Exception("Faults forwarding requires an error queue to be specified using 'EndpointConfiguration.SendFailedMessagesTo()'");
                }

                bool outBoxRunning = hostSettings.CoreSettings.IsFeatureActive(typeof(Features.Outbox));
                if (hostSettings.CoreSettings.TryGetAuditMessageExpiration(out var auditMessageExpiration))
                {
                    TimeToBeReceivedOverrideChecker.Check(
                        TransportTransactionMode != TransportTransactionMode.None,
                        outBoxRunning,
                        auditMessageExpiration > TimeSpan.Zero);
                }

                if (CreateQueuesForUser == null)
                {
                    // try to use the configured installer user in Core:
                    CreateQueuesForUser = hostSettings.CoreSettings.GetOrDefault<string>("Installers.UserName");
                }
            }

            if (hostSettings.SetupInfrastructure && CreateQueues)
            {
                var installerUser = GetInstallationUserName();
                var queueCreator = new MsmqQueueCreator(UseTransactionalQueues, installerUser);
                var queuesToCreate = receivers
                    .Select(r => r.ReceiveAddress)
                    .Concat(sendingAddresses)
                    .ToList();

                if (useTimeouts)
                {
                    await TimeoutStorage.Initialize(hostSettings.Name, cancellationToken).ConfigureAwait(false);
                    queuesToCreate.Add(TimeoutQueueAddress.ToString());
                    QueuePermissions.CheckQueue(TimeoutQueueAddress.ToString());
                }

                queueCreator.CreateQueueIfNecessary(queuesToCreate);
            }

            foreach (var address in sendingAddresses)
            {
                QueuePermissions.CheckQueue(address);
            }

            MessagePump timeoutsPump = null;
            if (useTimeouts)
            {
                QueuePermissions.CheckQueue(TimeoutQueueAddress.ToString());

                var timeoutsReceiver = new ReceiveSettings("Timeouts", TimeoutQueueAddress.ToString(), false, false, "error");
                timeoutsPump = new MessagePump(
                    mode => MsmqTransportInfrastructure.SelectReceiveStrategy(mode, TransactionScopeOptions.TransactionOptions),
                    MessageEnumeratorTimeout,
                    hostSettings.CriticalErrorAction,
                    this,
                    timeoutsReceiver
                    );
            }

            hostSettings.StartupDiagnostic.Add("NServiceBus.Transport.MSMQ", new
            {
                ExecuteInstaller = CreateQueues,
                UseDeadLetterQueue,
                UseConnectionCache,
                UseTransactionalQueues,
                UseJournalQueue,
                UseDeadLetterQueueForMessagesWithTimeToBeReceived,
                TimeToReachQueue = GetFormattedTimeToReachQueue(TimeToReachQueue),
                TimeoutQueue = TimeoutQueueAddress.ToString(),
                TimeoutStorageType = TimeoutStorage?.GetType(),
            });

            var msmqTransportInfrastructure = new MsmqTransportInfrastructure(this, timeoutsPump);
            await msmqTransportInfrastructure.SetupReceivers(receivers, hostSettings.CriticalErrorAction);

            return msmqTransportInfrastructure;
        }

        void ValidateIfDtcIsAvailable()
        {
            if (TransportTransactionMode == TransportTransactionMode.TransactionScope)
            {
                try
                {
                    using (var ts = new TransactionScope())
                    {
                        TransactionInterop.GetTransmitterPropagationToken(Transaction.Current); // Enforce promotion to MSDTC
                        ts.Complete();
                    }
                }
                catch (TransactionAbortedException)
                {
                    throw new Exception("Transaction mode is set to `TransactionScope`. This depends on Microsoft Distributed Transaction Coordinator (MSDTC) which is not available. Either enable MSDTC, enable Outbox, or lower the transaction mode to `SendsAtomicWithReceive`.");
                }
            }
        }

        /// <inheritdoc />
        public override string ToTransportAddress(QueueAddress address)
        {
            if (!address.Properties.TryGetValue("machine", out var machine))
            {
                machine = RuntimeEnvironment.MachineName;
            }
            if (!address.Properties.TryGetValue("queue", out var queueName))
            {
                queueName = address.BaseAddress;
            }

            var queue = new StringBuilder(queueName);
            if (address.Discriminator != null)
            {
                queue.Append("-" + address.Discriminator);
            }
            if (address.Qualifier != null)
            {
                queue.Append("." + address.Qualifier);
            }
            return $"{queue}@{machine}";
        }

        /// <inheritdoc />
        public override IReadOnlyCollection<TransportTransactionMode> GetSupportedTransactionModes() =>
            new TransportTransactionMode[]
            {
                TransportTransactionMode.None,
                TransportTransactionMode.ReceiveOnly,
                TransportTransactionMode.SendsAtomicWithReceive,
                TransportTransactionMode.TransactionScope,
            };

        /// <summary>
        /// Indicates whether queues should be automatically created. Setting this to <c>false</c> disables the automatic queue creation, even when installers are enabled using `EndpointConfiguration.EnableInstallers()`.
        /// </summary>
        /// <remarks>
        /// With installers enabled, required queues will be created automatically at startup.While this may be convenient for development,
        /// we instead recommend that queues are created as part of deployment using the CreateQueues.ps1 script included in the NuGet package.
        /// The installers might still need to be enabled to fulfill the installation needs of other components, but this method allows
        /// scripts to be used for queue creation instead.
        /// </remarks>
        public bool CreateQueues { get; set; } = true;

        /// <summary>
        /// This setting should be used with caution. Configures whether to store undeliverable messages in the dead letter queue. Disabling the dead letter queue should be used with caution. Setting this to <c>false</c> should only be used where loss of messages is an acceptable.
        /// </summary>
        public bool UseDeadLetterQueue { get; set; } = true;

        /// <summary>
        /// Configures MSMQ to cache connections to a remote queue and re-use them
        /// as needed instead of creating new connections for each message.
        /// Turning connection caching off will negatively impact the message throughput in most scenarios.
        /// </summary>
        public bool UseConnectionCache { get; set; } = true;

        /// <summary>
        /// This setting should be used with caution. When set to <c>false</c>, any message that has
        /// an exception during processing will not be rolled back to the queue. Therefore this setting must only
        /// be disabled when loss of messages is an acceptable scenario.
        /// </summary>
        public bool UseTransactionalQueues { get; set; } = true;

        /// <summary>
        /// Controls the use of journaling messages. When journaling is enabled, a copy of every message received will be stored in the journal queue. Disabled by default.
        /// Should be used ONLY when debugging as it can potentially use up the MSMQ journal storage quota based on the message volume.
        /// </summary>
        public bool UseJournalQueue { get; set; } = false;

        /// <summary>
        /// When enabled messages that have exceeded their TimeToBeReceived will be moved to the dead letter queue instead of discarding them. Disabled by default.
        /// </summary>
        public bool UseDeadLetterQueueForMessagesWithTimeToBeReceived { get; set; } = false;

        /// <summary>
        /// Configures the Time-To-Reach-Queue (TTRQ) timespan. The default value if not set is Message.InfiniteTimeout
        /// </summary>
        public TimeSpan TimeToReachQueue { get; set; } = Message.InfiniteTimeout;

        /// <summary>
        /// Set a delegate to use for applying the <see cref="Message.Label" /> property when sending a message.
        /// </summary>
        /// <remarks>
        /// This delegate will be used for all valid messages sent via MSMQ.
        /// This includes not just standard messages, but also Audits, Errors and all control messages.
        /// In some cases it may be useful to check the <see cref="Headers.ControlMessageHeader" /> key to determine if a message is
        /// a control message.
        /// The only exception to this rule is received messages with corrupted headers. These messages will be forwarded to the
        /// error queue with no label applied.
        /// </remarks>
        public Func<IReadOnlyDictionary<string, string>, string> ApplyCustomLabelToOutgoingMessages { get; set; } = _ => string.Empty;

        /// <summary>
        /// Configures whether to ignore incoming Time-To-Be-Received (TTBR) headers. By default an expired TTBR header will result in the message to be discarded.
        /// </summary>
        public bool IgnoreIncomingTimeToBeReceivedHeaders { get; set; } = false;

        /// <summary>
        /// When set to <c>true</c>, disables native Time-To-Be-Received (TTBR) when combined with transactions. Instead, the receiver will discard incoming messages that have exceeded the specified Time-To-Be-Received.
        /// </summary>
        public bool UseNonNativeTimeToBeReceivedInTransactions { get; set; } = false;

        /// <summary>
        /// The user account that will be configured with access rights to the queues created by the transport. When not set, the current user will be used.
        /// This setting is not relevant, if queue creation has been disabled.
        /// </summary>
        public string CreateQueuesForUser { get; set; }

        internal ITimeoutStorage TimeoutStorage { get; set; }

        internal MsmqAddress TimeoutQueueAddress { get; set; }

        /// <summary>
        /// Use timeouts managed via external storage
        /// </summary>
        public void UseTimeouts(ITimeoutStorage timeoutStorage, string timeoutsQueueAddress = null)
        {
            Guard.AgainstNull(nameof(timeoutStorage), timeoutStorage);
            TimeoutStorage = timeoutStorage;
            if (!string.IsNullOrEmpty(timeoutsQueueAddress))
            {
                TimeoutQueueAddress = MsmqAddress.Parse(timeoutsQueueAddress);
            }
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
        public void ConfigureTransactionScope(TimeSpan? timeout = null, IsolationLevel? isolationLevel = null)
        {
            Guard.AgainstNegativeAndZero(nameof(timeout), timeout);

            if (isolationLevel == IsolationLevel.Snapshot)
            {
                throw new ArgumentException("Isolation level `Snapshot` is not supported by the transport. Consider not sharing the transaction between transport and persistence if persistence should use `IsolationLevel.Snapshot` by using `TransportTransactionMode.SendsAtomicWithReceive` or lower.", nameof(isolationLevel));
            }

            TransactionScopeOptions = new MsmqScopeOptions(timeout, isolationLevel);
        }

        static string GetFormattedTimeToReachQueue(TimeSpan timeToReachQueue)
        {
            return timeToReachQueue == Message.InfiniteTimeout ? "Infinite"
                : string.Format("{0:%d} day(s) {0:%hh} hours(s) {0:%mm} minute(s) {0:%ss} second(s)", timeToReachQueue);
        }

        string GetInstallationUserName()
        {
            if (CreateQueuesForUser != null)
            {
                return CreateQueuesForUser;
            }

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                return $"{Environment.UserDomainName}\\{Environment.UserName}";
            }

            return Environment.UserName;
        }

        /// <summary>
        /// Defines the timeout used for <see cref="MessageEnumerator.MoveNext(TimeSpan)"/>.
        /// </summary>
        protected internal TimeSpan MessageEnumeratorTimeout { get; set; } = TimeSpan.FromSeconds(1);

        internal MsmqScopeOptions TransactionScopeOptions { get; private set; } = new MsmqScopeOptions();
    }
}
