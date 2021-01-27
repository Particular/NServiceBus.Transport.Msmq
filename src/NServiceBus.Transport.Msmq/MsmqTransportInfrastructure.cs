namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Collections.Generic;
    using System.Messaging;
    using System.Text;
    using System.Threading.Tasks;
    using System.Transactions;
    using Performance.TimeToBeReceived;
    using Routing;
    using Settings;
    using Support;
    using Transport;

    //TODO how to support non-durable delivery?
    class MsmqTransportInfrastructure : TransportInfrastructure
    {
        private readonly MsmqTransport transportSettings;
        private readonly bool outboxEnabled;

        public MsmqTransportInfrastructure(MsmqTransport transportSettings, bool outboxEnabled)
        {
            this.transportSettings = transportSettings;
            this.outboxEnabled = outboxEnabled;

            //TODO make non-virtual in core
            Dispatcher = new MsmqMessageDispatcher(transportSettings);
        }

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

        void SetupReceivers(ReceiveSettings[] receivers)
        {
            //TODO
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
                () => new MessagePump(guarantee => SelectReceiveStrategy(guarantee, msmqSettings.ScopeOptions.TransactionOptions), msmqSettings.MessageEnumeratorTimeout, !msmqSettings.IgnoreIncomingTimeToBeReceivedHeaders),
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

        public override Task Start()
        {
            settings.AddStartupDiagnosticsSection("NServiceBus.Transport.MSMQ", new
            {
                msmqSettings.ExecuteInstaller,
                msmqSettings.UseDeadLetterQueue,
                msmqSettings.UseConnectionCache,
                msmqSettings.UseTransactionalQueues,
                msmqSettings.UseJournalQueue,
                msmqSettings.UseDeadLetterQueueForMessagesWithTimeToBeReceived,
                TimeToReachQueue = GetFormattedTimeToReachQueue(msmqSettings.TimeToReachQueue)
            });

            return Task.FromResult(0);
        }

        static string GetFormattedTimeToReachQueue(TimeSpan timeToReachQueue)
        {
            return timeToReachQueue == Message.InfiniteTimeout ? "Infinite"
                : string.Format("{0:%d} day(s) {0:%hh} hours(s) {0:%mm} minute(s) {0:%ss} second(s)", timeToReachQueue);
        }


        public override TransportSubscriptionInfrastructure ConfigureSubscriptionInfrastructure()
        {
            throw new NotImplementedException("MSMQ does not support native pub/sub.");
        }

        public override Task DisposeAsync()
        {
            throw new NotImplementedException();
        }
    }
}