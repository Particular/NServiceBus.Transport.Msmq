namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using System.Transactions;
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

        public void SetupReceivers(ReceiveSettings[] receivers, Action<string, Exception> criticalErrorAction)
        {
            var messagePumps = new List<IMessageReceiver>(receivers.Length);

            foreach (var receiver in receivers)
            {
                if (receiver.UsePublishSubscribe)
                {
                    throw new NotImplementedException("MSMQ does not support native pub/sub.");
                }

                // The following check avoids creating some sub-queues, if the endpoint sub queue has the capability to exceed the max length limitation for queue format name.
                CheckEndpointNameComplianceForMsmq.Check(receiver.ReceiveAddress);
                QueuePermissions.CheckQueue(receiver.ReceiveAddress);

                var pump = new MessagePump(
                    transactionMode =>
                        SelectReceiveStrategy(transactionMode, transportSettings.TransactionScopeOptions.TransactionOptions),
                    transportSettings.MessageEnumeratorTimeout,
                    transportSettings.IgnoreIncomingTimeToBeReceivedHeaders,
                    criticalErrorAction,
                    transportSettings,
                    receiver);
                messagePumps.Add(pump);
            }

            Receivers = messagePumps.AsReadOnly();
        }

        public override Task DisposeAsync()
        {
            foreach (var receiver in Receivers)
            {
                (receiver as MessagePump)?.Dispose();
            }

            return Task.CompletedTask;
        }
    }
}