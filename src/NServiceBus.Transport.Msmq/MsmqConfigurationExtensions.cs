namespace NServiceBus
{
    using System;
    using System.Collections.Generic;
    using Configuration.AdvancedExtensibility;
    using Routing;
    using Transport.Msmq;

    /// <summary>
    /// Adds extensions methods to <see cref="TransportExtensions{T}" /> for configuration purposes.
    /// </summary>
    public static partial class MsmqConfigurationExtensions
    {

        /// <summary>
        /// Configure the endpoint to use the MSMQ transport.
        /// </summary>
        /// <param name="endpointConfiguration">this endpoint configuration.</param>
        /// <param name="transport">The MSMQ transport.</param>
        public static RoutingSettings<MsmqTransport> UseTransport(this EndpointConfiguration endpointConfiguration, MsmqTransport transport)
        {
            // Required feature
            endpointConfiguration.EnableFeature<InstanceMappingFileFeature>();

            // Call into the more general UseTransport<T> from Core
            return endpointConfiguration.UseTransport<MsmqTransport>(transport);
        }

        /// <summary>
        /// Sets a distribution strategy for a given endpoint.
        /// </summary>
        /// <param name="config">MSMQ Transport configuration object.</param>
        /// <param name="distributionStrategy">The instance of a distribution strategy.</param>
        public static void SetMessageDistributionStrategy(this RoutingSettings<MsmqTransport> config, DistributionStrategy distributionStrategy)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(distributionStrategy);

            config.GetSettings().GetOrCreate<List<DistributionStrategy>>().Add(distributionStrategy);
        }

        /// <summary>
        /// Returns the configuration options for the file based instance mapping file.
        /// </summary>
        /// <param name="config">MSMQ Transport configuration object.</param>
        public static InstanceMappingFileSettings InstanceMappingFile(this RoutingSettings<MsmqTransport> config)
        {
            ArgumentNullException.ThrowIfNull(config);
            return new InstanceMappingFileSettings(config.GetSettings());
        }
    }
}