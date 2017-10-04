namespace NServiceBus.Transport.Msmq.Tests.Persistence
{
    using System;
    using NServiceBus.Persistence.Msmq;
    using NUnit.Framework;

    [TestFixture]
    public class MsmqSubscriptionPersistenceTests
    {
        [Test]
        public void ShouldThrowIfOldDefaultSubscriptionQueuePresent()
        {
            // Create queue "NServiceBus.Subscriptions" to simulate the presence of the old default queue. 
            const string oldDefaultQueue = "NServiceBus.Subscriptions";
            var queuePath = MsmqAddress.Parse(oldDefaultQueue).PathWithoutPrefix;
            MsmqHelpers.CreateQueue(queuePath);

            var persistence = new MsmqSubscriptionPersistence();
            Assert.IsTrue(persistence.DoesOldDefaultQueueExists());
            Assert.Throws<Exception>(() => persistence.ThrowIfUsingTheOldDefaultSubscriptionsQueue(""));

            // Delete the queue
            MsmqHelpers.DeleteQueue(queuePath);
        }

        [Test]
        public void ShouldNotThrowIfDefaultSubscriptionQueueIsConfigured()
        {
            // Create queue "NServiceBus.Subscriptions" to simulate the presence of the old default queue. 
            const string oldDefaultQueue = "NServiceBus.Subscriptions";
            var queuePath = MsmqAddress.Parse(oldDefaultQueue).PathWithoutPrefix;
            MsmqHelpers.CreateQueue(queuePath);

            var persistence = new MsmqSubscriptionPersistence();
            Assert.IsTrue(persistence.DoesOldDefaultQueueExists());
            Assert.DoesNotThrow(() => persistence.ThrowIfUsingTheOldDefaultSubscriptionsQueue("NServiceBus.Subscriptions"));

            // Delete the queue
            MsmqHelpers.DeleteQueue(queuePath);
        }
    }
}