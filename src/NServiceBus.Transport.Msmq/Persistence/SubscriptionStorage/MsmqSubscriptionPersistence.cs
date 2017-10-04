namespace NServiceBus.Persistence.Msmq
{
    using System;
    using System.Messaging;
    using Features;
    using Logging;
    using Transport;
    using Transport.Msmq;

    class MsmqSubscriptionPersistence : Feature
    {
        protected override void Setup(FeatureConfigurationContext context)
        {
            var configuredQueueName = context.Settings.GetConfiguredMsmqPersistenceSubscriptionQueue();

            if (string.IsNullOrEmpty(configuredQueueName))
            {
                if (DoesOldDefaultQueueExists())
                {
                    // The user has not configured the subscriptions queue to be "NServiceBus.Subscriptions" but there's a local queue. 
                    // Indicates that the endoint was using old default queue name.
                    throw new Exception(
                        "Detected the presence of an old default queue named `NServiceBus.Subscriptions`. The new default is now `[Your endpointname].Subscriptions`. Move the relevant subscription messages to the new queue or configure the subscriptions queue name.");
                }

                var defaultQueueName = $"{context.Settings.EndpointName()}.Subscriptions";
                Logger.Warn($"The queue used to store subscriptions has not been configured, the default '{defaultQueueName}' will be used.");
                configuredQueueName = defaultQueueName;
            }

            context.Settings.Get<QueueBindings>().BindSending(configuredQueueName);

            var useTransactionalStorageQueue = true;
            MsmqSettings msmqSettings;

            if (context.Settings.TryGet(out msmqSettings))
            {
                useTransactionalStorageQueue = msmqSettings.UseTransactionalQueues;
            }

            context.Container.ConfigureComponent(b =>
            {
                var queue = new MsmqSubscriptionStorageQueue(MsmqAddress.Parse(configuredQueueName), useTransactionalStorageQueue);
                return new MsmqSubscriptionStorage(queue);
            }, DependencyLifecycle.SingleInstance);
        }

        bool DoesOldDefaultQueueExists()
        {
            const string oldDefaultSubscriptionsQueue = "NServiceBus.Subscriptions";
            var path = MsmqAddress.Parse(oldDefaultSubscriptionsQueue).PathWithoutPrefix;
            return MessageQueue.Exists(path);
        }

        static ILog Logger = LogManager.GetLogger(typeof(MsmqSubscriptionPersistence));
    }
}