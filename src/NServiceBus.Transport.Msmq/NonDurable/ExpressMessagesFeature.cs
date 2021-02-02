namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Reflection;
    using Features;

    class ExpressMessagesFeature : Feature
    {
        internal const string ExpressMessageConventionSettingsKey = "messageDurabilityConvention";

        public ExpressMessagesFeature()
        {
            EnableByDefault();
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
            if (!context.Settings.TryGet<Func<Type, bool>>(
                ExpressMessageConventionSettingsKey,
                out var durabilityConvention))
            {
                durabilityConvention = t => t.GetCustomAttribute<ExpressAttribute>(true) != null;
            }

            context.Pipeline.Register(new DetermineMessageDurabilityBehavior(durabilityConvention), "Adds the NonDurableDelivery constraint for messages that have requested to be delivered in non-durable mode");
        }
    }
}