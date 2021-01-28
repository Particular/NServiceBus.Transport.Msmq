using System;
using System.Collections.Generic;
using System.Messaging;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Transport;
using NServiceBus.TransportTests;

class ConfigureMsmqTransportInfrastructure : IConfigureTransportInfrastructure
{
    private string receiveQueue;

    public TransportDefinition CreateTransportDefinition()
    {
        var msmqSettings = new MsmqTransport();
        msmqSettings.MessageEnumeratorTimeout = TimeSpan.FromMilliseconds(10);
        msmqSettings.IgnoreIncomingTimeToBeReceivedHeaders = true;

        return msmqSettings;
    }

    public async Task<TransportInfrastructure> Configure(TransportDefinition transportDefinition, HostSettings hostSettings, string inputQueueName, string errorQueueName)
    {
        var msmqSettings = (MsmqTransport) transportDefinition;
        receiveQueue = inputQueueName;
        var infrastructure = await msmqSettings.Initialize(hostSettings,
            new[] {new ReceiveSettings("TestReceiver", inputQueueName, false, false, errorQueueName)},
            new[] {errorQueueName});

        return infrastructure;
    }

    public Task Cleanup()
    {
        var allQueues = MessageQueue.GetPrivateQueuesByMachine("localhost");
        var queuesToBeDeleted = new List<string>();

        foreach (var messageQueue in allQueues)
        {
            using (messageQueue)
            {
                var indexOfAt = receiveQueue.IndexOf("@", StringComparison.Ordinal);
                if (indexOfAt >= 0)
                {
                    receiveQueue = receiveQueue.Substring(0, indexOfAt);
                }

                if (messageQueue.QueueName.StartsWith(@"private$\" + receiveQueue, StringComparison.OrdinalIgnoreCase))
                {
                    queuesToBeDeleted.Add(messageQueue.Path);
                }
            }
        }

        foreach (var queuePath in queuesToBeDeleted)
        {
            try
            {
                MessageQueue.Delete(queuePath);
                Console.WriteLine("Deleted '{0}' queue", queuePath);
            }
            catch (Exception)
            {
                Console.WriteLine("Could not delete queue '{0}'", queuePath);
            }
        }

        MessageQueue.ClearConnectionCache();

        return Task.FromResult(0);
    }
}