namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Transport;

    class MsmqTransportInfrastructure : TransportInfrastructure
    {
        readonly MsmqTransport transportSettings;

        public MsmqTransportInfrastructure(MsmqTransport transportSettings)
        {
            this.transportSettings = transportSettings;

            Dispatcher = new MsmqMessageDispatcher(transportSettings);
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