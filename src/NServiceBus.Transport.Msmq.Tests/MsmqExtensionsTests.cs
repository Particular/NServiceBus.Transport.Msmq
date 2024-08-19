namespace NServiceBus.Transport.Msmq.Tests
{
    using System.Security.Principal;
    using NUnit.Framework;
    using Particular.Msmq;
    using Support;

    [TestFixture]
    class MsmqExtensionsTests
    {
        static readonly string LocalEveryoneGroupName = new SecurityIdentifier(WellKnownSidType.WorldSid, null).Translate(typeof(NTAccount)).ToString();

        string path;
        MessageQueue queue;

        [OneTimeSetUp]
        public void Setup()
        {
            var queueName = "permissionsTest";
            path = $@"{RuntimeEnvironment.MachineName}\private$\{queueName}";
            MsmqHelpers.DeleteQueue(path);
            MsmqHelpers.CreateQueue(path);

            queue = new MessageQueue(path);
        }

        [OneTimeTearDown]
        public void Teardown()
        {
            queue.Dispose();
            MsmqHelpers.DeleteQueue(path);
        }

        [TestCase(AccessControlEntryType.Allow)]
        [TestCase(AccessControlEntryType.Deny)]
        public void GetPermissions_returns_queue_access_rights(AccessControlEntryType providedAccessType)
        {
            queue.SetPermissions(LocalEveryoneGroupName, MessageQueueAccessRights.WriteMessage | MessageQueueAccessRights.ReceiveMessage, providedAccessType);
            if (!queue.TryGetPermissions(LocalEveryoneGroupName, out var rights, out var accessType))
            {
                Assert.Fail($"Unable to read permissions for queue: {queue.QueueName}");
            }

            Assert.Multiple(() =>
            {
                Assert.That(rights.HasValue, Is.True);
                Assert.That(rights.Value.HasFlag(MessageQueueAccessRights.WriteMessage), Is.True);
                Assert.That(rights.Value.HasFlag(MessageQueueAccessRights.ReceiveMessage), Is.True);
                Assert.That(accessType, Is.EqualTo(providedAccessType));
            });
        }
    }
}