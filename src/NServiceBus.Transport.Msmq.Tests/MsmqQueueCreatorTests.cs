namespace NServiceBus.Transport.Msmq.Tests
{
    using System;
    using System.Messaging;
    using System.Security.Principal;
    using NUnit.Framework;

    [TestFixture]
    public class MsmqQueueCreatorTests
    {
        const string testQueueNameForSending = "NServiceBus.Core.Tests.MsmqQueueCreatorTests.Sending";
        const string testQueueNameForReceiving = "NServiceBus.Core.Tests.MsmqQueueCreatorTests.Receiving";

        [SetUp]
        public void Setup()
        {
            DeleteQueueIfPresent(testQueueNameForSending);
            DeleteQueueIfPresent(testQueueNameForReceiving);
        }

        [TearDown]
        public void TearDown()
        {
            DeleteQueueIfPresent(testQueueNameForSending);
            DeleteQueueIfPresent(testQueueNameForReceiving);
        }

        [Test]
        public void Should_create_all_queues()
        {
            var creator = new MsmqQueueCreator(true, WindowsIdentity.GetCurrent().Name);

            creator.CreateQueueIfNecessary(new[]
            {
                testQueueNameForReceiving,
                testQueueNameForSending
            });

            Assert.True(QueueExists(testQueueNameForSending));
            Assert.True(QueueExists(testQueueNameForReceiving));
        }

        [Test]
        public void Should_not_create_queue_when_a_remote_queue_is_provided()
        {
            var remoteQueueName = $"{testQueueNameForReceiving}@some-machine";
            var creator = new MsmqQueueCreator(true, WindowsIdentity.GetCurrent().Name);

            creator.CreateQueueIfNecessary(new[]
            {
                remoteQueueName
            });

            Assert.False(QueueExists(testQueueNameForReceiving));
        }


        [Test]
        public void Should_setup_permissions()
        {
            // use the network service account since that one won't be in the local admin group
            var creator = new MsmqQueueCreator(true, NetworkServiceAccountName);

            creator.CreateQueueIfNecessary(new[] { testQueueNameForReceiving });

            var createdQueue = GetQueue(testQueueNameForReceiving);

            Assert.True(createdQueue.TryGetPermissions(NetworkServiceAccountName, out var accountAccessRights, out var accessControlEntryType));
            Assert.True(accountAccessRights.HasValue);
            Assert.True(accessControlEntryType == AccessControlEntryType.Allow, "User should have access");
            Assert.True(accountAccessRights.Value.HasFlag(MessageQueueAccessRights.WriteMessage), $"{NetworkServiceAccountName} should have write access");
            Assert.True(accountAccessRights.Value.HasFlag(MessageQueueAccessRights.ReceiveMessage), $"{NetworkServiceAccountName} should have receive messages access");
            Assert.True(accountAccessRights.Value.HasFlag(MessageQueueAccessRights.GetQueueProperties), $"{NetworkServiceAccountName} should have get queue properties access");

            Assert.True(createdQueue.TryGetPermissions(LocalAdministratorsGroupName, out var localAdminAccessRights, out var accessControlEntryTypeForLocalAdmin));
            Assert.True(localAdminAccessRights.HasValue);
            Assert.True(localAdminAccessRights.Value.HasFlag(MessageQueueAccessRights.FullControl), $"{LocalAdministratorsGroupName} should have full control");
            Assert.IsTrue(accessControlEntryTypeForLocalAdmin == AccessControlEntryType.Allow, $"{LocalAdministratorsGroupName} should have access");
        }

        [Test]
        public void Should_make_queues_transactional_if_requested()
        {
            var creator = new MsmqQueueCreator(true, WindowsIdentity.GetCurrent().Name);

            creator.CreateQueueIfNecessary(new[] { testQueueNameForReceiving });

            var queue = GetQueue(testQueueNameForReceiving);

            Assert.True(queue.Transactional);
        }

        [Test]
        public void Should_make_queues_non_transactional_if_requested()
        {
            var creator = new MsmqQueueCreator(false, WindowsIdentity.GetCurrent().Name);

            creator.CreateQueueIfNecessary(new[] { testQueueNameForReceiving });

            var queue = GetQueue(testQueueNameForReceiving);

            Assert.False(queue.Transactional);
        }

        [Test]
        public void Should_not_add_everyone_and_anonymous_to_already_existing_queues()
        {
            var path = MsmqAddress.Parse(testQueueNameForReceiving).PathWithoutPrefix;

            using (var queue = MessageQueue.Create(path))
            {
                queue.SetPermissions(LocalEveryoneGroupName, MessageQueueAccessRights.GenericWrite, AccessControlEntryType.Revoke);
                queue.SetPermissions(LocalAnonymousLogonName, MessageQueueAccessRights.WriteMessage, AccessControlEntryType.Revoke);
            }

            var creator = new MsmqQueueCreator(true, WindowsIdentity.GetCurrent().Name);

            creator.CreateQueueIfNecessary(new[] { testQueueNameForReceiving });

            var existingQueue = GetQueue(testQueueNameForReceiving);
            Assert.False(existingQueue.TryGetPermissions(LocalEveryoneGroupName, out _, out _));
            Assert.False(existingQueue.TryGetPermissions(LocalAnonymousLogonName, out _, out _));
        }


        [Test]
        public void Should_blow_up_for_invalid_accounts()
        {
            var creator = new MsmqQueueCreator(true, "invalidaccount");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                creator.CreateQueueIfNecessary(new[] { testQueueNameForReceiving }));

            StringAssert.Contains("invalidaccount", ex.Message);
        }

        MessageQueue GetQueue(string queueName)
        {
            var path = MsmqAddress.Parse(queueName).PathWithoutPrefix;

            return new MessageQueue(path);
        }

        bool QueueExists(string queueName)
        {
            var path = MsmqAddress.Parse(queueName).PathWithoutPrefix;

            return MessageQueue.Exists(path);
        }

        void DeleteQueueIfPresent(string queueName)
        {
            var path = MsmqAddress.Parse(queueName).PathWithoutPrefix;

            if (MessageQueue.Exists(path))
            {
                MessageQueue.Delete(path);
            }
        }


        static readonly string LocalEveryoneGroupName = new SecurityIdentifier(WellKnownSidType.WorldSid, null).Translate(typeof(NTAccount)).ToString();
        static readonly string LocalAnonymousLogonName = new SecurityIdentifier(WellKnownSidType.AnonymousSid, null).Translate(typeof(NTAccount)).ToString();
        static readonly string NetworkServiceAccountName = new SecurityIdentifier(WellKnownSidType.NetworkServiceSid, null).Translate(typeof(NTAccount)).ToString();
        static readonly string LocalAdministratorsGroupName = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Translate(typeof(NTAccount)).ToString();
    }
}
