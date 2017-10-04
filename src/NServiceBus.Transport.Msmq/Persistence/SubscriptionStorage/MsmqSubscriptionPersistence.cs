namespace NServiceBus.Persistence.Msmq
{
    using Features;
    using Logging;
    using Transport;
    using Transport.Msmq;

    class MsmqSubscriptionPersistence : Feature
    {
        protected override void Setup(FeatureConfigurationContext context)
        {
            var queueName = context.Settings.GetConfiguredMsmqPersistenceSubscriptionQueue();

            if (string.IsNullOrEmpty(queueName))
            {
                queueName = $"{context.Settings.EndpointName()}.Subscriptions";
                Logger.Warn($"The queue used to store subscriptions has not been configured, so the default '{queueName}' will be used.");
            }

            context.Settings.Get<QueueBindings>().BindSending(queueName);

            var useTransactionalStorageQueue = true;
            MsmqSettings msmqSettings;

            if (context.Settings.TryGet(out msmqSettings))
            {
                useTransactionalStorageQueue = msmqSettings.UseTransactionalQueues;
            }

            context.Container.ConfigureComponent(b =>
            {
                var queue = new MsmqSubscriptionStorageQueue(MsmqAddress.Parse(queueName), useTransactionalStorageQueue);
                return new MsmqSubscriptionStorage(queue);
            }, DependencyLifecycle.SingleInstance);
        }

        static ILog Logger = LogManager.GetLogger(typeof(MsmqSubscriptionPersistence));
    }
}