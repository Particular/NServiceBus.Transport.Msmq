namespace NServiceBus.Transport.Msmq.Tests
{
    using System;
    using Particular.Msmq;
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

            Assert.That(QueueExists(testQueueNameForSending), Is.True);
            Assert.That(QueueExists(testQueueNameForReceiving), Is.True);
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

            Assert.That(QueueExists(testQueueNameForReceiving), Is.False);
        }


        [Test]
        public void Should_setup_permissions()
        {
            // use the network service account since that one won't be in the local admin group
            var creator = new MsmqQueueCreator(true, NetworkServiceAccountName);

            creator.CreateQueueIfNecessary(new[] { testQueueNameForReceiving });

            var createdQueue = GetQueue(testQueueNameForReceiving);

            Assert.That(createdQueue.TryGetPermissions(NetworkServiceAccountName, out var accountAccessRights, out var accessControlEntryType), Is.True);
            Assert.That(accountAccessRights.HasValue, Is.True);
            Assert.That(accessControlEntryType == AccessControlEntryType.Allow, Is.True, "User should have access");
            Assert.That(accountAccessRights.Value.HasFlag(MessageQueueAccessRights.WriteMessage), Is.True, $"{NetworkServiceAccountName} should have write access");
            Assert.That(accountAccessRights.Value.HasFlag(MessageQueueAccessRights.ReceiveMessage), Is.True, $"{NetworkServiceAccountName} should have receive messages access");
            Assert.That(accountAccessRights.Value.HasFlag(MessageQueueAccessRights.GetQueueProperties), Is.True, $"{NetworkServiceAccountName} should have get queue properties access");

            Assert.That(createdQueue.TryGetPermissions(LocalAdministratorsGroupName, out var localAdminAccessRights, out var accessControlEntryTypeForLocalAdmin), Is.True);
            Assert.That(localAdminAccessRights.HasValue, Is.True);
            Assert.That(localAdminAccessRights.Value.HasFlag(MessageQueueAccessRights.FullControl), Is.True, $"{LocalAdministratorsGroupName} should have full control");
            Assert.That(accessControlEntryTypeForLocalAdmin == AccessControlEntryType.Allow, Is.True, $"{LocalAdministratorsGroupName} should have access");
        }

        [Test]
        public void Should_make_queues_transactional_if_requested()
        {
            var creator = new MsmqQueueCreator(true, WindowsIdentity.GetCurrent().Name);

            creator.CreateQueueIfNecessary(new[] { testQueueNameForReceiving });

            var queue = GetQueue(testQueueNameForReceiving);

            Assert.That(queue.Transactional, Is.True);
        }

        [Test]
        public void Should_make_queues_non_transactional_if_requested()
        {
            var creator = new MsmqQueueCreator(false, WindowsIdentity.GetCurrent().Name);

            creator.CreateQueueIfNecessary(new[] { testQueueNameForReceiving });

            var queue = GetQueue(testQueueNameForReceiving);

            Assert.That(queue.Transactional, Is.False);
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
            Assert.That(existingQueue.TryGetPermissions(LocalEveryoneGroupName, out _, out _), Is.False);
            Assert.That(existingQueue.TryGetPermissions(LocalAnonymousLogonName, out _, out _), Is.False);
        }


        [Test]
        public void Should_blow_up_for_invalid_accounts()
        {
            var creator = new MsmqQueueCreator(true, "invalidaccount");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                creator.CreateQueueIfNecessary(new[] { testQueueNameForReceiving }));

            Assert.That(ex.Message, Does.Contain("invalidaccount"));
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
