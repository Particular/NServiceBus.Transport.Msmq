namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading.Tasks;
    using System.Transactions;
    using Performance.TimeToBeReceived;
    using Routing;
    using Support;
    using Transport;

    class MsmqTransportInfrastructure : TransportInfrastructure
    {
        public MsmqTransportInfrastructure(MsmqSettings msmqSettings, QueueBindings queueBindings, MsmqScopeOptions scopeOptions, Func<IReadOnlyDictionary<string, string>, string> messageLabelGenerator, bool isTransactional, bool outBoxRunning, TimeSpan auditMessageExpiration)
        {
            this.msmqSettings = msmqSettings;
            this.queueBindings = queueBindings;
            this.scopeOptions = scopeOptions;
            this.messageLabelGenerator = messageLabelGenerator;
            this.isTransactional = isTransactional;
            this.outBoxRunning = outBoxRunning;
            this.auditMessageExpiration = auditMessageExpiration;
        }

        public override IEnumerable<Type> DeliveryConstraints { get; } = new[]
        {
            typeof(DiscardIfNotReceivedBefore),
            typeof(NonDurableDelivery)
        };

        public override TransportTransactionMode TransactionMode { get; } = TransportTransactionMode.TransactionScope;
        public override OutboundRoutingPolicy OutboundRoutingPolicy { get; } = new OutboundRoutingPolicy(OutboundRoutingType.Unicast, OutboundRoutingType.Unicast, OutboundRoutingType.Unicast);

        ReceiveStrategy SelectReceiveStrategy(TransportTransactionMode minimumConsistencyGuarantee, TransactionOptions transactionOptions)
        {
            switch (minimumConsistencyGuarantee)
            {
                case TransportTransactionMode.TransactionScope:
                    return new TransactionScopeStrategy(transactionOptions, new MsmqFailureInfoStorage(1000));
                case TransportTransactionMode.SendsAtomicWithReceive:
                    return new SendsAtomicWithReceiveNativeTransactionStrategy(new MsmqFailureInfoStorage(1000));
                case TransportTransactionMode.ReceiveOnly:
                    return new ReceiveOnlyNativeTransactionStrategy(new MsmqFailureInfoStorage(1000));
                case TransportTransactionMode.None:
                    return new NoTransactionStrategy();
                default:
                    throw new NotSupportedException($"TransportTransactionMode {minimumConsistencyGuarantee} is not supported by the MSMQ transport");
            }
        }

        public override EndpointInstance BindToLocalEndpoint(EndpointInstance instance) => instance.AtMachine(RuntimeEnvironment.MachineName);

        public override string ToTransportAddress(LogicalAddress logicalAddress)
        {
            if (!logicalAddress.EndpointInstance.Properties.TryGetValue("machine", out var machine))
            {
                machine = RuntimeEnvironment.MachineName;
            }
            if (!logicalAddress.EndpointInstance.Properties.TryGetValue("queue", out var queueName))
            {
                queueName = logicalAddress.EndpointInstance.Endpoint;
            }
            var queue = new StringBuilder(queueName);
            if (logicalAddress.EndpointInstance.Discriminator != null)
            {
                queue.Append("-" + logicalAddress.EndpointInstance.Discriminator);
            }
            if (logicalAddress.Qualifier != null)
            {
                queue.Append("." + logicalAddress.Qualifier);
            }
            return $"{queue}@{machine}";
        }

        public override string MakeCanonicalForm(string transportAddress)
        {
            return MsmqAddress.Parse(transportAddress).ToString();
        }

        public override TransportReceiveInfrastructure ConfigureReceiveInfrastructure()
        {
            CheckMachineNameForCompliance.Check();

            // The following check avoids creating some sub-queues, if the endpoint sub queue has the capability to exceed the max length limitation for queue format name.
            foreach (var queue in queueBindings.ReceivingAddresses)
            {
                CheckEndpointNameComplianceForMsmq.Check(queue);
            }

            return new TransportReceiveInfrastructure(
                () => new MessagePump(guarantee => SelectReceiveStrategy(guarantee, scopeOptions.TransactionOptions)),
                () =>
                {
                    if (msmqSettings.ExecuteInstaller)
                    {
                        return new MsmqQueueCreator(msmqSettings.UseTransactionalQueues);
                    }
                    return new NullQueueCreator();
                },
                () =>
                {
                    foreach (var address in queueBindings.ReceivingAddresses)
                    {
                        QueuePermissions.CheckQueue(address);
                    }
                    return Task.FromResult(StartupCheckResult.Success);
                });
        }

        public override TransportSendInfrastructure ConfigureSendInfrastructure()
        {
            CheckMachineNameForCompliance.Check();

            return new TransportSendInfrastructure(
                () => new MsmqMessageDispatcher(msmqSettings, messageLabelGenerator),
                () =>
                {
                    foreach (var address in queueBindings.SendingAddresses)
                    {
                        QueuePermissions.CheckQueue(address);
                    }

                    var auditTTBROverridden = auditMessageExpiration > TimeSpan.Zero;
                    var result = TimeToBeReceivedOverrideChecker.Check(isTransactional, outBoxRunning, auditTTBROverridden);
                    return Task.FromResult(result);
                });
        }

        public override TransportSubscriptionInfrastructure ConfigureSubscriptionInfrastructure()
        {
            throw new NotImplementedException("MSMQ does not support native pub/sub.");
        }

        MsmqSettings msmqSettings;
        QueueBindings queueBindings;
        MsmqScopeOptions scopeOptions;
        Func<IReadOnlyDictionary<string, string>, string> messageLabelGenerator;
        bool isTransactional;
        bool outBoxRunning;
        TimeSpan auditMessageExpiration;
    }
}