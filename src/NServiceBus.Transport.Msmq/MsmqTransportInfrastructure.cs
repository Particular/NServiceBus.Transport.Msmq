
namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Transactions;
    using Transport;

    class MsmqTransportInfrastructure : TransportInfrastructure
    {
        readonly MsmqTransport transportSettings;
        readonly MessagePump timeoutsPump;
        readonly ITimeoutStorage timeoutStorage;
        readonly TimeoutPoller timeoutPoller;

        public MsmqTransportInfrastructure(MsmqTransport transportSettings, MsmqMessageDispatcher dispatcher, MessagePump timeoutsPump, TimeoutPoller timeoutPoller)
        {
            this.transportSettings = transportSettings;
            this.timeoutsPump = timeoutsPump;
            this.timeoutPoller = timeoutPoller;
            timeoutStorage = transportSettings.DelayedDeliverySettings.TimeoutStorage;

            Dispatcher = dispatcher;

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
                await timeoutsPump.Initialize(PushRuntimeSettings.Default, OnTimeoutMessageReceived, OnTimeoutError, CancellationToken.None).ConfigureAwait(false);
                await timeoutsPump.StartReceive(CancellationToken.None).ConfigureAwait(false);

                timeoutPoller.Start();
            }
        }

        Task<ErrorHandleResult> OnTimeoutError(ErrorContext errorcontext, CancellationToken cancellationtoken)
        {
            // TODO: implement the on error
            throw new NotImplementedException();
        }

        async Task OnTimeoutMessageReceived(MessageContext context, CancellationToken cancellationtoken)
        {
            try
            {
                var isTimeout = context.Headers.Any(x => x.Key.StartsWith(MsmqUtilities.PropertyHeaderPrefix));

                if (!isTimeout)
                {
                    throw new Exception("This message does not represent a timeout");
                }

                var id = context.Headers[Headers.MessageId];
                var destination = context.Headers[MsmqUtilities.PropertyHeaderPrefix + MsmqMessageDispatcher.TimeoutDestination];
                var at = DateTimeOffsetHelper.ToDateTimeOffset(context.Headers[MsmqUtilities.PropertyHeaderPrefix + MsmqMessageDispatcher.TimeoutAt]);

                var message = context.Extensions.Get<System.Messaging.Message>();

                var diff = DateTime.UtcNow - at;

                if (diff.Ticks > 0) // Due
                {
                    await Dispatcher.Dispatch(id, message.Extension, context.Body, destination, context.TransportTransaction).ConfigureAwait(false);
                }
                else
                {
                    var timeout = new TimeoutItem
                    {
                        Destination = destination,
                        Id = id,
                        State = context.Body,
                        Time = at.UtcDateTime,
                        Headers = message.Extension
                    };

                    await timeoutStorage.Store(timeout).ConfigureAwait(false);

                    timeoutPoller.Callback(timeout.Time);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                // TODO: Seems no recoverability is executed
                throw;
            }
        }

        public override Task Shutdown(CancellationToken cancellationToken = default)
        {
            timeoutsPump?.StopReceive(cancellationToken);
            timeoutPoller?.Stop();
            return Task.CompletedTask;
        }
    }
}