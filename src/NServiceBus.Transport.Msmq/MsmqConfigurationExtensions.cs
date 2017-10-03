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
        /// Disables the automatic queue creation when the endpoint configuration, EnableInstaller is called.
        /// </summary>
        /// <remarks>
        /// If EnableInstallers() is called during endpoint configuration, the endpoint will create the queues required automatically. 
        /// It's a much better scenario to create the queues once by running the included powershell scripts instead of 
        /// calling EnableInstallers every time on startup. However the EnableInstallers might be used to run other installation code, such as persistence setup. 
        /// In the case of MSMQ Transport, calling EnableInstallers creates the queues necessary for the endpoint. This Api on the 
        /// transport, explicitly disables that. Call DisableInstaller to not create the queues when the endpoint is starting.
        /// </remarks>
        public static void DisableInstaller(this TransportExtensions<MsmqTransport> config)
        {
            Guard.AgainstNull(nameof(config), config);
            config.GetSettings().Set(MsmqTransport.DisableInstaller, true);
        }
    }
}
