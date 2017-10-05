namespace NServiceBus.Transport.Msmq.Tests.Persistence
{
    using System;
    using NServiceBus.Persistence.Msmq;
    using NUnit.Framework;
    using Settings;

    [TestFixture]
    public class MsmqSubscriptionPersistenceTests
    {
        const string oldDefaultQueue = "NServiceBus.Subscriptions";

        [SetUp]
        public void Setup()
        {
            // Create queue "NServiceBus.Subscriptions" to simulate the presence of the old default queue.
            var queuePath = MsmqAddress.Parse(oldDefaultQueue).PathWithoutPrefix;
            MsmqHelpers.CreateQueue(queuePath);
        }

        [TearDown]
        public void TearDown()
        {
            var queuePath = MsmqAddress.Parse(oldDefaultQueue).PathWithoutPrefix;
            // Delete the queue
            MsmqHelpers.DeleteQueue(queuePath);
        }

        [Test]
        public void ShouldThrowIfStorageQueueNotConfiguredAndOldDefaultIsPresent()
        {
            Assert.Throws<Exception>(() => MsmqSubscriptionPersistence.DetermineStorageQueueName(PrepareSettings(configuredStorageQueueName: null)));
        }

        [Test]
        public void ShouldUseConfiguredStorageQueueEvenIfOldDefaultIsPresent()
        {
            const string customStorageQueue = "MyStorageQueue";

            Assert.AreEqual(customStorageQueue, MsmqSubscriptionPersistence.DetermineStorageQueueName(PrepareSettings(configuredStorageQueueName: customStorageQueue)));
        }

        static ReadOnlySettings PrepareSettings(string endpointName = "MyEndpoint", string configuredStorageQueueName = null)
        {
            var settings = new SettingsHolder();

            settings.Set("NServiceBus.Routing.EndpointName", endpointName);

            if (!string.IsNullOrEmpty(configuredStorageQueueName))
            {

                settings.Set(MsmqSubscriptionStorageConfigurationExtensions.MsmqPersistenceQueueConfigurationKey, configuredStorageQueueName);
            }

            return settings;
        }
    }
}