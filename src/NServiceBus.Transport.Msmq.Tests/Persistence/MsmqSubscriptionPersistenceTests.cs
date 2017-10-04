namespace NServiceBus.Transport.Msmq.Tests.Persistence
{
    using System;
    using NServiceBus.Persistence.Msmq;
    using NUnit.Framework;

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
        public void ShouldThrowIfOldDefaultSubscriptionQueuePresent()
        {
            var persistence = new MsmqSubscriptionPersistence();
            Assert.IsTrue(persistence.DoesOldDefaultQueueExists());
            Assert.Throws<Exception>(() => persistence.ThrowIfUsingTheOldDefaultSubscriptionsQueue(""));
        }

        [Test]
        public void ShouldNotThrowIfDefaultSubscriptionQueueIsConfigured()
        {
            var persistence = new MsmqSubscriptionPersistence();
            Assert.IsTrue(persistence.DoesOldDefaultQueueExists());
            Assert.DoesNotThrow(() => persistence.ThrowIfUsingTheOldDefaultSubscriptionsQueue("NServiceBus.Subscriptions"));
        }
    }
}