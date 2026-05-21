namespace NServiceBus.Transport.Msmq;

using System.Security;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Particular.Msmq;

class QueuePermissions
{
    public static void CheckQueue(string address, ILogger<QueuePermissions> logger)
    {
        var msmqAddress = MsmqAddress.Parse(address);
        var queuePath = msmqAddress.PathWithoutPrefix;

        logger.LogDebug($"Checking if queue exists: {queuePath}.");
        if (msmqAddress.IsRemote())
        {
            logger.LogInformation($"Since {address} is remote, the queue could not be verified. Make sure the queue exists and that the address and permissions are correct. Messages could end up in the dead letter queue if configured incorrectly.");
            return;
        }

        var path = msmqAddress.PathWithoutPrefix;

        try
        {
            if (MessageQueue.Exists(path))
            {
                using var messageQueue = new MessageQueue(path);
                logger.LogDebug("Verified that the queue: [{0}] exists", queuePath);
                WarnIfPublicAccess(messageQueue, LocalEveryoneGroupName, logger);
                WarnIfPublicAccess(messageQueue, LocalAnonymousLogonName, logger);
            }
            else
            {
                logger.LogWarning("Queue [{0}] does not exist", queuePath);
            }
        }
        catch (MessageQueueException ex)
        {
            logger.LogWarning($"Unable to verify queue at address '{queuePath}'. Make sure the queue exists, and that the address is correct. Processing will still continue.", ex);
        }
    }

    static void WarnIfPublicAccess(MessageQueue queue, string userGroupName, ILogger<QueuePermissions> logger)
    {
        MessageQueueAccessRights? accessRights;
        AccessControlEntryType? accessType;

        try
        {
            queue.TryGetPermissions(userGroupName, out accessRights, out accessType);
        }
        catch (SecurityException se)
        {
            logger.LogWarning($"Unable to read permissions for queue [{queue.QueueName}]. Make sure you have administrative access on the target machine", se);
            return;
        }

        if (accessType == AccessControlEntryType.Allow)
        {
            var logMessage = $"Queue [{queue.QueueName}] is running with [{userGroupName}] with AccessRights set to [{accessRights}]. Consider setting appropriate permissions, if required by the organization. For more information, consult the documentation.";
            logger.LogWarning(logMessage);
        }
    }

    static readonly string LocalEveryoneGroupName = new SecurityIdentifier(WellKnownSidType.WorldSid, null).Translate(typeof(NTAccount)).ToString();
    static readonly string LocalAnonymousLogonName = new SecurityIdentifier(WellKnownSidType.AnonymousSid, null).Translate(typeof(NTAccount)).ToString();
}