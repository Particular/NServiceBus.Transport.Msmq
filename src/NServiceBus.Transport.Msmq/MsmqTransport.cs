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
    using Faults;
    using Features;
    using Routing;
    using Support;
    using Transport;
    using Transport.Msmq;

    /// <summary>
    /// Transport definition for MSMQ.
    /// </summary>
    public partial class MsmqTransport : TransportDefinition, IMessageDrivenSubscriptionTransport
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

            var queuesToCreate = receivers
                .Select(r => r.ReceiveAddress)
                .Concat(sendingAddresses)
                .ToHashSet();

            string timeoutsQueue = null;
            string timeoutsErrorQueue = null;
            var requiresDelayedDelivery = DelayedDelivery != null;

            if (requiresDelayedDelivery)
            {
                if (receivers.Length > 0)
                {
                    var mainReceiver = receivers[0];
                    var mainQueueAddress = MsmqAddress.Parse(mainReceiver.ReceiveAddress);
                    timeoutsQueue = new MsmqAddress($"{mainQueueAddress.Queue}{TimeoutQueueSuffix}", mainQueueAddress.Machine).ToString();
                    timeoutsErrorQueue = mainReceiver.ErrorQueue;
                }
                else
                {
                    if (hostSettings.CoreSettings != null)
                    {
                        if (!hostSettings.CoreSettings.TryGetExplicitlyConfiguredErrorQueueAddress(out var coreErrorQueue))
                        {
                            throw new Exception("Delayed delivery requires an error queue to be specified using 'EndpointConfiguration.SendFailedMessagesTo()'");
                        }

                        timeoutsErrorQueue = coreErrorQueue;
                        timeoutsQueue = MsmqAddress.Parse($"{hostSettings.Name}{TimeoutQueueSuffix}").ToString(); //Use name of the endpoint as the timeouts queue name. Use local machine
                    }
                    else
                    {
                        throw new Exception("Timeouts are not supported for send-only configurations outside of an NServiceBus endpoint.");
                    }
                }
                queuesToCreate.Add(timeoutsQueue);
                queuesToCreate.Add(timeoutsErrorQueue);
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

                if (requiresDelayedDelivery)
                {
                    await DelayedDelivery.TimeoutStorage.Initialize(hostSettings.Name, cancellationToken).ConfigureAwait(false);
                }

                queueCreator.CreateQueueIfNecessary(queuesToCreate);
            }

            foreach (var address in sendingAddresses)
            {
                QueuePermissions.CheckQueue(address);
            }

            var dispatcher = new MsmqMessageDispatcher(this, timeoutsQueue, OnSendCallbackForTesting);

            DelayedDeliveryPump delayedDeliveryPump = null;
            TimeoutPoller timeoutPoller = null;
            if (requiresDelayedDelivery)
            {
                QueuePermissions.CheckQueue(timeoutsQueue);
                QueuePermissions.CheckQueue(timeoutsErrorQueue);

                var staticFaultMetadata = new Dictionary<string, string>
                {
                    {FaultsHeaderKeys.FailedQ, timeoutsQueue},
                    {Headers.ProcessingMachine, RuntimeEnvironment.MachineName},
                    {Headers.ProcessingEndpoint, hostSettings.Name},
                    {Headers.HostDisplayName, hostSettings.HostDisplayName}
                };

                timeoutPoller = new TimeoutPoller(dispatcher, DelayedDelivery.TimeoutStorage, DelayedDelivery.NumberOfRetries, hostSettings.CriticalErrorAction, timeoutsErrorQueue, staticFaultMetadata);

                var delayedDeliveryMessagePump = new MessagePump(mode => SelectReceiveStrategy(mode, TransactionScopeOptions.TransactionOptions),
                    MessageEnumeratorTimeout, TransportTransactionMode, false, hostSettings.CriticalErrorAction,
                    new ReceiveSettings("DelayedDelivery", timeoutsQueue, false, false, timeoutsErrorQueue));

                delayedDeliveryPump = new DelayedDeliveryPump(dispatcher, timeoutPoller, DelayedDelivery.TimeoutStorage, delayedDeliveryMessagePump, timeoutsErrorQueue, DelayedDelivery.NumberOfRetries, hostSettings.CriticalErrorAction, DelayedDelivery.TimeToTriggerStoreCircuitBreaker, staticFaultMetadata);
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
                TimeoutQueue = timeoutsQueue,
                TimeoutStorageType = DelayedDelivery?.TimeoutStorage?.GetType(),
            });

            var infrastructure = new MsmqTransportInfrastructure(CreateReceivers(receivers, hostSettings.CriticalErrorAction), dispatcher, delayedDeliveryPump, timeoutPoller);
            await infrastructure.Start().ConfigureAwait(false);

            return infrastructure;
        }

        Dictionary<string, IMessageReceiver> CreateReceivers(ReceiveSettings[] receivers, Action<string, Exception, CancellationToken> criticalErrorAction)
        {
            var messagePumps = new Dictionary<string, IMessageReceiver>(receivers.Length);

            foreach (var receiver in receivers)
            {
                if (receiver.UsePublishSubscribe)
                {
                    throw new NotImplementedException("MSMQ does not support native pub/sub.");
                }

                // The following check avoids creating some sub-queues, if the endpoint sub queue has the capability to exceed the max length limitation for queue format name.
                CheckEndpointNameComplianceForMsmq.Check(receiver.ReceiveAddress);
                QueuePermissions.CheckQueue(receiver.ReceiveAddress);

                var pump = new MessagePump(
                    transactionMode =>
                        SelectReceiveStrategy(transactionMode, TransactionScopeOptions.TransactionOptions),
                    MessageEnumeratorTimeout,
                    TransportTransactionMode,
                    IgnoreIncomingTimeToBeReceivedHeaders,
                    criticalErrorAction,
                    receiver
                );

                messagePumps.Add(pump.Id, pump);
            }

            return messagePumps;
        }

        static ReceiveStrategy SelectReceiveStrategy(TransportTransactionMode minimumConsistencyGuarantee, TransactionOptions transactionOptions)
        {
            switch (minimumConsistencyGuarantee)
            {
                case TransportTransactionMode.TransactionScope:
                    return new TransactionScopeStrategy(transactionOptions, new MsmqFailureInfoStorage(1000));
                case TransportTransactionMode.SendsAtomicWithReceive:
                    return new SendsAtomicWithReceiveNativeTransactionStrategy(new MsmqFailureInfoStorage(1000));
                case TransportTransactionMode.ReceiveOnly:
                    return new ReceiveOnlyNativeTransactionStrategy(new MsmqFailureInfoStorage(1000));
                case TransportTransactionMode.None:
                    return new NoTransactionStrategy();
                default:
                    throw new NotSupportedException($"TransportTransactionMode {minimumConsistencyGuarantee} is not supported by the MSMQ transport");
            }
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
            new[]
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

        /// <summary>
        /// Use timeouts managed via external storage
        /// </summary>
        public DelayedDeliverySettings DelayedDelivery { get; set; }

        /// <summary>
        /// The callback that can be used to inject failures to the dispatcher for testing.
        /// </summary>
        internal Action<TransportTransaction, UnicastTransportOperation> OnSendCallbackForTesting { get; set; }

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
