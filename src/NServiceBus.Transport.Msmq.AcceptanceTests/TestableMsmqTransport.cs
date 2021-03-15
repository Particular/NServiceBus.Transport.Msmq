using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Transport;

/// <summary>
/// A dedicated subclass of the MsmqTransport that enables us to intercept the receive queues for the test.
/// </summary>
class TestableMsmqTransport : MsmqTransport
{
    public string[] ReceiveQueues = new string[0];

    public override Task<TransportInfrastructure> Initialize(HostSettings hostSettings, ReceiveSettings[] receivers, string[] sendingAddresses, CancellationToken cancellationToken = default)
    {
        MessageEnumeratorTimeout = TimeSpan.FromMilliseconds(10);
        ReceiveQueues = receivers.Select(r => r.ReceiveAddress).ToArray();

        return base.Initialize(hostSettings, receivers, sendingAddresses, cancellationToken);
    }
}