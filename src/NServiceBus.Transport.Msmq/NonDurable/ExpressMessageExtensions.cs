namespace NServiceBus.Transport.Msmq
{
    using System;
    using Configuration.AdvancedExtensibility;

    /// <summary>
    /// Provides extension methods to configure express messages.
    /// </summary>
    public static class ExpressMessageExtensions
    {
        /// <summary>
        /// Sets the function to be used to evaluate whether a type is an express message or not.
        /// </summary>
        public static void DefiningExpressMessagesAs(this RoutingSettings<MsmqTransport> routingSettings, Func<Type, bool> expressMessageConvention)
        {
            Guard.AgainstNull(nameof(routingSettings), routingSettings);
            Guard.AgainstNull(nameof(expressMessageConvention), expressMessageConvention);

            routingSettings.GetSettings().Set(ExpressMessagesFeature.ExpressMessageConventionSettingsKey, expressMessageConvention);
        }
    }
}