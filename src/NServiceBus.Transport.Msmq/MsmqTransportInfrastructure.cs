using System.Linq;
using System.Runtime.Remoting.Messaging;

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
        readonly MessagePump timeoutsPump;
        readonly ITimeoutStorage timeoutStorage;

        public MsmqTransportInfrastructure(MsmqTransport transportSettings, MessagePump timeoutsPump = null, ITimeoutStorage timeoutStorage = null)
        {
            this.transportSettings = transportSettings;
            this.timeoutsPump = timeoutsPump;
            this.timeoutStorage = timeoutStorage;

            Dispatcher = new MsmqMessageDispatcher(transportSettings);
        }

        public static ReceiveStrategy SelectReceiveStrategy(TransportTransactionMode minimumConsistencyGuarantee, TransactionOptions transactionOptions)
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

        public Task SetupReceivers(ReceiveSettings[] receivers, Action<string, Exception, CancellationToken> criticalErrorAction)
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
                    receiver
                    );

                messagePumps.Add(pump.Id, pump);
            }

            Receivers = messagePumps;

            return StartTimeoutPump();
        }

        async Task StartTimeoutPump()
        {
            if (timeoutsPump != null)
            {
                await timeoutsPump.Initialize(PushRuntimeSettings.Default, OnTimeoutMessageReceived, OnTimeoutError, CancellationToken.None);
                await timeoutsPump.StartReceive(CancellationToken.None);
            }
        }

        Task<ErrorHandleResult> OnTimeoutError(ErrorContext errorcontext, CancellationToken cancellationtoken)
        {
            // TODO: implement the on error
            throw new NotImplementedException();
        }

        async Task OnTimeoutMessageReceived(MessageContext context, CancellationToken cancellationtoken)
        {
            var isTimeout = context.Headers.Any(x=> x.Key.StartsWith(MsmqUtilities.PropertyHeaderPrefix));

            if (!isTimeout)
            {
                throw new Exception("This message does not represent a timeout");
            }

            var id = context.Headers[Headers.MessageId];
            var destination = context.Headers[MsmqUtilities.PropertyHeaderPrefix + MsmqMessageDispatcher.TimeoutDestination];
            var at = DateTimeOffsetHelper.ToDateTimeOffset(
                context.Headers[MsmqUtilities.PropertyHeaderPrefix + MsmqMessageDispatcher.TimeoutAt]);

            var timeout = new TimeoutItem
            {
                Destination = destination, Id = id, State = context.Body, Time = at.UtcDateTime
            };

            await timeoutStorage.Store(timeout).ConfigureAwait(false);
        }

        public override Task Shutdown(CancellationToken cancellationToken = default)
        {
            timeoutsPump?.StopReceive(cancellationToken);
            return Task.CompletedTask;
        }
    }
}