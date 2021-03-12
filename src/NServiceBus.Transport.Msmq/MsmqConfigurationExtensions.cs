namespace NServiceBus
{
    using System.Collections.Generic;
    using Configuration.AdvancedExtensibility;
    using Transport.Msmq;
    using Routing;

    /// <summary>
    /// Adds extensions methods to <see cref="TransportExtensions{T}" /> for configuration purposes.
    /// </summary>
    public static partial class MsmqConfigurationExtensions
    {
        /// <summary>
        /// Configures the endpoint to use MSMQ to send and receive messages.
        /// </summary>
        [ObsoleteEx(
            ReplacementTypeOrMember = "EndpointConfiguration.UseTransport(TransportDefinition)",
            RemoveInVersion = "4",
            TreatAsErrorFromVersion = "3")]
        public static MsmqTransportSettings UseTransport<TTransport>(this EndpointConfiguration endpointConfiguration)
            where TTransport : MsmqTransport
        {
            var msmqTransport = new MsmqTransport();
            var routingSettings = endpointConfiguration.UseTransport(msmqTransport);
            return new MsmqTransportSettings(msmqTransport, routingSettings);
        }

        /// <summary>
        /// Sets a distribution strategy for a given endpoint.
        /// </summary>
        /// <param name="config">MSMQ Transport configuration object.</param>
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
        /// <param name="config">MSMQ Transport configuration object.</param>
        public static InstanceMappingFileSettings InstanceMappingFile(this RoutingSettings<MsmqTransport> config)
        {
            Guard.AgainstNull(nameof(config), config);
            return new InstanceMappingFileSettings(config.GetSettings());
        }
    }
}