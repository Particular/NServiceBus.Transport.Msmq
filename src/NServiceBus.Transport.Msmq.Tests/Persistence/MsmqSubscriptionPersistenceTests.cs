namespace NServiceBus.Transport.Msmq.Tests.Persistence
{
    using System;
    using NServiceBus.Persistence.Msmq;
    using NUnit.Framework;
    using Settings;

    [TestFixture]
    public class MsmqSubscriptionPersistenceTests
    {
        [SetUp]
        public void Setup()
        {
            // Create queue "NServiceBus.Subscriptions" to simulate the presence of the old default queue.
            queuePath = MsmqAddress.Parse(oldDefaultQueue).PathWithoutPrefix;
            MsmqHelpers.CreateQueue(queuePath);
        }

        [TearDown]
        public void TearDown()
        {
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

        [Test]
        public void ShouldDefaultStorageQueueIfNotConfiguredAndNoOldLegacyQueueIsPresent()
        {
            const string endpointName = "MyEndpoint";

            MsmqHelpers.DeleteQueue(queuePath);

            Assert.AreEqual($"{endpointName}.Subscriptions", MsmqSubscriptionPersistence.DetermineStorageQueueName(PrepareSettings(endpointName)));
        }

        static ReadOnlySettings PrepareSettings(string endpointName = "DefaultEndpoint", string configuredStorageQueueName = null)
        {
            var settings = new SettingsHolder();

            settings.Set("NServiceBus.Routing.EndpointName", endpointName);

            if (!string.IsNullOrEmpty(configuredStorageQueueName))
            {
                settings.Set(MsmqSubscriptionStorageConfigurationExtensions.MsmqPersistenceQueueConfigurationKey, configuredStorageQueueName);
            }

            return settings;
        }

        string queuePath;
        const string oldDefaultQueue = "NServiceBus.Subscriptions";
    }
}