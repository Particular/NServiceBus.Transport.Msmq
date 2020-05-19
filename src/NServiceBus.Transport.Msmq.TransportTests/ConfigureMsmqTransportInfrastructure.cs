using System;
using System.Collections.Generic;
using System.Linq;
using System.Messaging;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Settings;
using NServiceBus.Transport;
using NServiceBus.TransportTests;

class ConfigureMsmqTransportInfrastructure : IConfigureTransportInfrastructure
{
    public TransportConfigurationResult Configure(SettingsHolder settings, TransportTransactionMode transactionMode)
    {
        CreateStartupDiagnostics(settings);

        var msmqTransportDefinition = new MsmqTransport();
        settingsHolder = settings;
        settingsHolder.Set("NServiceBus.Transport.Msmq.MessageEnumeratorTimeout", TimeSpan.FromMilliseconds(10));
        return new TransportConfigurationResult
        {
            TransportInfrastructure = msmqTransportDefinition.Initialize(settingsHolder, null),
            PurgeInputQueueOnStartup = true
        };
    }

    public Task Cleanup()
    {
        var queueBindings = settingsHolder.Get<QueueBindings>();
        var allQueues = MessageQueue.GetPrivateQueuesByMachine("localhost");
        var queuesToBeDeleted = new List<string>();

        foreach (var messageQueue in allQueues)
        {
            using (messageQueue)
            {
                if (queueBindings.ReceivingAddresses.Any(ra =>
                {
                    var indexOfAt = ra.IndexOf("@", StringComparison.Ordinal);
                    if (indexOfAt >= 0)
                    {
                        ra = ra.Substring(0, indexOfAt);
                    }

                    return messageQueue.QueueName.StartsWith(@"private$\" + ra, StringComparison.OrdinalIgnoreCase);
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

        return Task.FromResult(0);
    }

    static void CreateStartupDiagnostics(SettingsHolder settings)
    {
        var ctor = hostingSettingsType.GetConstructors()[0];
        var hostingSettings = ctor.Invoke(new object[] {settings});
        settings.Set(hostingSettingsType.FullName, hostingSettings);
    }

    SettingsHolder settingsHolder;

    static Type hostingSettingsType = typeof(IEndpointInstance).Assembly.GetType("NServiceBus.HostingComponent+Settings", true);
}