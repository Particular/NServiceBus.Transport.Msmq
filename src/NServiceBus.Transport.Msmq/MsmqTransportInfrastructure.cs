namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Transactions;
    using Transport;

    class MsmqTransportInfrastructure : TransportInfrastructure
    {
        readonly MsmqTransport transportSettings;

        public MsmqTransportInfrastructure(MsmqTransport transportSettings)
        {
            this.transportSettings = transportSettings;

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

        public void SetupReceivers(ReceiveSettings[] receivers, Action<string, Exception, CancellationToken> criticalErrorAction)
        {
            var messagePumps = new Dictionary<string, IMessageReceiver>(receivers.Length);

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
                    criticalErrorAction,
                    transportSettings,
                    receiver);
                messagePumps.Add(pump.Id, pump);
            }

            Receivers = messagePumps;
        }

        public override Task Shutdown(CancellationToken cancellationToken)
        {
            foreach (var receiver in Receivers.Values)
            {
                (receiver as MessagePump)?.Dispose();
            }

            return Task.CompletedTask;
        }
    }
}