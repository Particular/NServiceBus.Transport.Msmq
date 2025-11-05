using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.AcceptanceTesting.Support;
using Particular.Msmq;

public class ConfigureEndpointMsmqTransport : IConfigureEndpointTestExecution
{
    // Need to reference the testable transport as a regular transport so that the correct extension method overload
    // gets called so that the required InstanceMappingFileFeature is enabled in the acceptance tests
    readonly MsmqTransport transportDefinition = new TestableMsmqTransport();

#pragma warning disable PS0018 // A task-returning method should have a CancellationToken parameter unless it has a parameter implementing ICancellableContext
    public Task Configure(string endpointName, EndpointConfiguration configuration, RunSettings settings, PublisherMetadata publisherMetadata)
#pragma warning restore PS0018 // A task-returning method should have a CancellationToken parameter unless it has a parameter implementing ICancellableContext
    {
        transportDefinition.UseConnectionCache = false;
        transportDefinition.IgnoreIncomingTimeToBeReceivedHeaders = true;

        var routingConfig = configuration.UseTransport(transportDefinition);

        foreach (var publisher in publisherMetadata.Publishers)
        {
            foreach (var eventType in publisher.Events)
            {
                routingConfig.RegisterPublisher(eventType, publisher.PublisherName);
            }
        }

        return Task.CompletedTask;
    }

#pragma warning disable PS0018 // A task-returning method should have a CancellationToken parameter unless it has a parameter implementing ICancellableContext
    public Task Cleanup()
#pragma warning restore PS0018 // A task-returning method should have a CancellationToken parameter unless it has a parameter implementing ICancellableContext
    {
        var allQueues = MessageQueue.GetPrivateQueuesByMachine("localhost");
        var queuesToBeDeleted = new List<string>();

        var testableTransport = (TestableMsmqTransport)transportDefinition;

        foreach (var messageQueue in allQueues)
        {
            using (messageQueue)
            {
                if (testableTransport.ReceiveQueues.Any(ra =>
                {
                    var indexOfAt = ra.IndexOf("@", StringComparison.Ordinal);
                    if (indexOfAt >= 0)
                    {
                        ra = ra[..indexOfAt];
                    }
                    return messageQueue.QueueName.StartsWith($@"private$\{ra}", StringComparison.OrdinalIgnoreCase);
                }))
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

        return Task.CompletedTask;
    }
}
