
namespace NServiceBus.Transport.Msmq
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Transport;

    class MsmqTransportInfrastructure : TransportInfrastructure
    {
        readonly DelayedDeliveryPump delayedDeliveryPump;
        readonly TimeoutPoller timeoutPoller;

        public MsmqTransportInfrastructure(IReadOnlyDictionary<string, IMessageReceiver> receivers, MsmqMessageDispatcher dispatcher, DelayedDeliveryPump delayedDeliveryPump, TimeoutPoller timeoutPoller)
        {
            this.delayedDeliveryPump = delayedDeliveryPump;
            this.timeoutPoller = timeoutPoller;
            Dispatcher = dispatcher;
            Receivers = receivers;
        }

        public async Task Start(CancellationToken cancellationToken = default)
        {
            if (delayedDeliveryPump != null)
            {
                await delayedDeliveryPump.Start(cancellationToken).ConfigureAwait(false);
                timeoutPoller.Start();
            }
        }

        public override async Task Shutdown(CancellationToken cancellationToken = default)
        {
            if (delayedDeliveryPump != null)
            {
                await delayedDeliveryPump.Stop(cancellationToken).ConfigureAwait(false);
                await timeoutPoller.Stop(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}