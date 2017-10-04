namespace NServiceBus
{
    using System;
    using System.Collections.Generic;
    using System.Messaging;
    using System.Transactions;
    using Configuration.AdvancedExtensibility;
    using Routing;
    using Transport.Msmq;

    /// <summary>
    /// Adds extensions methods to <see cref="TransportExtensions{T}" /> for configuration purposes.
    /// </summary>
    public static class MsmqConfigurationExtensions
    {
        /// <summary>
        /// Set a delegate to use for applying the <see cref="Message.Label" /> property when sending a message.
        /// </summary>
        /// <remarks>
        /// This delegate will be used for all valid messages sent via MSMQ.
        /// This includes, not just standard messages, but also Audits, Errors and all control messages.
        /// In some cases it may be useful to use the <see cref="Headers.ControlMessageHeader" /> key to determine if a message is
        /// a control message.
        /// The only exception to this rule is received messages with corrupted headers. These messages will be forwarded to the
        /// error queue with no label applied.
        /// </remarks>
        public static TransportExtensions<MsmqTransport> ApplyLabelToMessages(this TransportExtensions<MsmqTransport> transportExtensions, Func<IReadOnlyDictionary<string, string>, string> labelGenerator)
        {
            Guard.AgainstNull(nameof(transportExtensions), transportExtensions);
            Guard.AgainstNull(nameof(labelGenerator), labelGenerator);
            transportExtensions.GetSettings().Set("msmqLabelGenerator", labelGenerator);
            return transportExtensions;
        }

        /// <summary>
        /// Allows to change the transaction isolation level and timeout for the `TransactionScope` used to receive messages.
        /// </summary>
        /// <remarks>
        /// If not specified the default transaction timeout of the machine will be used and the isolation level will be set to
        /// `ReadCommited`.
        /// </remarks>
        public static TransportExtensions<MsmqTransport> TransactionScopeOptions(this TransportExtensions<MsmqTransport> transportExtensions, TimeSpan? timeout = null, IsolationLevel? isolationLevel = null)
        {
            Guard.AgainstNull(nameof(transportExtensions), transportExtensions);
            Guard.AgainstNegativeAndZero(nameof(timeout), timeout);
            transportExtensions.GetSettings().Set<MsmqScopeOptions>(new MsmqScopeOptions(timeout, isolationLevel));
            return transportExtensions;
        }
        
        /// <summary>
        /// Sets a distribution strategy for a given endpoint.
        /// </summary>
        /// <param name="config">Config object.</param>
        /// <param name="distributionStrategy">The instance of a distribution strategy.</param>
        public static void SetMessageDistributionStrategy(this RoutingSettings<MsmqTransport> config, DistributionStrategy distributionStrategy)
        {
            Guard.AgainstNull(nameof(config), config);
            Guard.AgainstNull(nameof(distributionStrategy), distributionStrategy);
            config.GetSettings().GetOrCreate<List<DistributionStrategy>>().Add(distributionStrategy);
        }

        /// <summary>
        /// Returns the configuration options for the file based instance mapping file.
        /// </summary>
        public static InstanceMappingFileSettings InstanceMappingFile(this RoutingSettings<MsmqTransport> config)
        {
            Guard.AgainstNull(nameof(config), config);
            return new InstanceMappingFileSettings(config.GetSettings());
        }

        /// <summary>
        /// Moves messages that have exceeded their TimeToBeReceived to the dead letter queue instead of discarding them.
        /// </summary>
        public static void UseDeadLetterQueueForMessagesWithTimeToBeReceived(this TransportExtensions<MsmqTransport> config)
        {
            Guard.AgainstNull(nameof(config), config);
            config.GetSettings().Set(MsmqTransport.UseDeadLetterQueueForMessagesWithTimeToBeReceived, true);
        }

        /// <summary>
        /// Disables the automatic queue creation when installers are enabled using `EndpointConfiguration.EnableInstallers()`.
        /// </summary>
        /// <remarks>
        /// With installers enabled required queues will be created automatically at startup. While this is convenient for development 
        /// we instead recommend that queues are created as part of deployment using the `CreateQueues.ps1` script included in the NuGet package.
        /// Since installers might need to be enabled to installation needs of other components this method allows scripts to be used for queue creation
        /// even for scenarios where the NServiceBus installers needs to be enabled.
        /// </remarks>
        public static void DisableInstaller(this TransportExtensions<MsmqTransport> config)
        {
            Guard.AgainstNull(nameof(config), config);
            config.GetSettings().Set(MsmqTransport.DisableInstaller, true);
        }
    }
}
