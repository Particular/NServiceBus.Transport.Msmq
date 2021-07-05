
namespace NServiceBus.Transport.Msmq
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using DelayedDelivery;
    using Transport;

    class MsmqTransportInfrastructure : TransportInfrastructure
    {
        readonly DelayedDeliveryPump delayedDeliveryPump;
        readonly DueDelayedMessagePoller dueDelayedMessagePoller;

        public MsmqTransportInfrastructure(IReadOnlyDictionary<string, IMessageReceiver> receivers, MsmqMessageDispatcher dispatcher, DelayedDeliveryPump delayedDeliveryPump, DueDelayedMessagePoller dueDelayedMessagePoller)
        {
            this.delayedDeliveryPump = delayedDeliveryPump;
            this.dueDelayedMessagePoller = dueDelayedMessagePoller;
            Dispatcher = dispatcher;
            Receivers = receivers;
        }

        public async Task Start(CancellationToken cancellationToken = default)
        {
            if (delayedDeliveryPump != null)
            {
                await delayedDeliveryPump.Start(cancellationToken).ConfigureAwait(false);
                dueDelayedMessagePoller.Start();
            }
        }

        public override async Task Shutdown(CancellationToken cancellationToken = default)
        {
            if (delayedDeliveryPump != null)
            {
                await delayedDeliveryPump.Stop(cancellationToken).ConfigureAwait(false);
                await dueDelayedMessagePoller.Stop(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}