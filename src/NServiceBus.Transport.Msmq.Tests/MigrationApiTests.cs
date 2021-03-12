#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CS0618
namespace NServiceBus.Transport.Msmq.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Transactions;
    using Configuration.AdvancedExtensibility;
    using NUnit.Framework;

    [TestFixture]
    public class MigrationApiTests
    {
        [Test]
        public void UseTransport_should_configure_default_values()
        {
            var defaultSettings = new MsmqTransport();
            var endpointConfiguration = new EndpointConfiguration(nameof(MigrationApiTests));
            endpointConfiguration.UseTransport<MsmqTransport>();

            var configuredTransport = (MsmqTransport)endpointConfiguration.GetSettings().Get<TransportDefinition>();
            Assert.AreEqual(defaultSettings.IgnoreIncomingTimeToBeReceivedHeaders, configuredTransport.IgnoreIncomingTimeToBeReceivedHeaders);
            Assert.AreEqual(defaultSettings.TransactionScopeOptions.TransactionOptions.IsolationLevel, configuredTransport.TransactionScopeOptions.TransactionOptions.IsolationLevel);
            Assert.AreEqual(defaultSettings.TransactionScopeOptions.TransactionOptions.Timeout, configuredTransport.TransactionScopeOptions.TransactionOptions.Timeout);
            Assert.AreEqual(defaultSettings.UseJournalQueue, configuredTransport.UseJournalQueue);
            Assert.AreEqual(defaultSettings.UseConnectionCache, configuredTransport.UseConnectionCache);
            Assert.AreEqual(defaultSettings.ApplyCustomLabelToOutgoingMessages, configuredTransport.ApplyCustomLabelToOutgoingMessages);
            Assert.AreEqual(defaultSettings.CreateQueues, configuredTransport.CreateQueues);
            Assert.AreEqual(defaultSettings.CreateQueuesForUser, configuredTransport.CreateQueuesForUser);
            Assert.AreEqual(defaultSettings.TimeToReachQueue, configuredTransport.TimeToReachQueue);
            Assert.AreEqual(defaultSettings.UseDeadLetterQueue, configuredTransport.UseDeadLetterQueue);
            Assert.AreEqual(defaultSettings.UseDeadLetterQueueForMessagesWithTimeToBeReceived, configuredTransport.UseDeadLetterQueueForMessagesWithTimeToBeReceived);
            Assert.AreEqual(defaultSettings.UseNonNativeTimeToBeReceivedInTransactions, configuredTransport.UseNonNativeTimeToBeReceivedInTransactions);
            Assert.AreEqual(defaultSettings.UseTransactionalQueues, configuredTransport.UseTransactionalQueues);
        }

        [Test]
        public void UseTransport_should_apply_settings()
        {
            var endpointConfiguration = new EndpointConfiguration(nameof(MigrationApiTests));

            var settings = endpointConfiguration.UseTransport<MsmqTransport>();

            settings.ApplyLabelToMessages(_ => "testlabel");
            settings.TransactionScopeOptions(TimeSpan.FromSeconds(42), IsolationLevel.Chaos);
            settings.UseDeadLetterQueueForMessagesWithTimeToBeReceived();
            settings.DisableInstaller();
            settings.DisableDeadLetterQueueing();
            settings.DisableConnectionCachingForSends();
            settings.UseNonTransactionalQueues();
            settings.EnableJournaling();
            settings.TimeToReachQueue(TimeSpan.FromMinutes(42));
            settings.DisableNativeTimeToBeReceivedInTransactions();
            settings.IgnoreIncomingTimeToBeReceivedHeaders();

            var configuredTransport = (MsmqTransport)endpointConfiguration.GetSettings().Get<TransportDefinition>();

            Assert.AreEqual("testlabel", configuredTransport.ApplyCustomLabelToOutgoingMessages(new Dictionary<string, string>()));
            Assert.AreEqual(IsolationLevel.Chaos, configuredTransport.TransactionScopeOptions.TransactionOptions.IsolationLevel);
            Assert.AreEqual(TimeSpan.FromSeconds(42), configuredTransport.TransactionScopeOptions.TransactionOptions.Timeout);
            Assert.IsTrue(configuredTransport.UseDeadLetterQueueForMessagesWithTimeToBeReceived);
            Assert.IsFalse(configuredTransport.CreateQueues);
            Assert.IsFalse(configuredTransport.UseDeadLetterQueue);
            Assert.IsFalse(configuredTransport.UseConnectionCache);
            Assert.IsFalse(configuredTransport.UseTransactionalQueues);
            Assert.IsTrue(configuredTransport.UseJournalQueue);
            Assert.AreEqual(TimeSpan.FromMinutes(42), configuredTransport.TimeToReachQueue);
            Assert.True(configuredTransport.UseNonNativeTimeToBeReceivedInTransactions);
            Assert.True(configuredTransport.IgnoreIncomingTimeToBeReceivedHeaders);
        }
    }
}
#pragma warning restore CS0618
#pragma warning restore IDE0079 // Remove unnecessary suppression