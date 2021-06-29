namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Logging;
    using Routing;

    class DelayedDeliveryPump
    {
        readonly MsmqMessageDispatcher dispatcher;
        readonly TimeoutPoller poller;
        readonly ITimeoutStorage storage;
        readonly int numberOfRetries;
        readonly MessagePump pump;
        readonly Dictionary<string, string> faultMetadata;
        readonly string errorQueue;
        RepeatedFailuresOverTimeCircuitBreaker storeCircuitBreaker;
        readonly ILog Log = LogManager.GetLogger<DelayedDeliveryPump>();

        public DelayedDeliveryPump(MsmqMessageDispatcher dispatcher,
            TimeoutPoller poller,
            ITimeoutStorage storage,
            MessagePump messagePump,
            string errorQueue,
            int numberOfRetries,
            Action<string, Exception, CancellationToken> criticalErrorAction,
            TimeSpan timeToWaitForStoreCircuitBreaker,
            Dictionary<string, string> faultMetadata)
        {
            this.dispatcher = dispatcher;
            this.poller = poller;
            this.storage = storage;
            this.numberOfRetries = numberOfRetries;
            this.faultMetadata = faultMetadata;
            pump = messagePump;
            this.errorQueue = errorQueue;

            storeCircuitBreaker = new RepeatedFailuresOverTimeCircuitBreaker("DelayedDeliveryStore", timeToWaitForStoreCircuitBreaker,
                ex => criticalErrorAction("Failed to store delayed message", ex, CancellationToken.None));
        }

        public async Task Start()
        {
            await pump.Initialize(PushRuntimeSettings.Default, TimeoutReceived, OnError, CancellationToken.None).ConfigureAwait(false);
            await pump.StartReceive(CancellationToken.None).ConfigureAwait(false);
        }

        public Task Stop(CancellationToken cancellationToken)
        {
            return pump.StopReceive(cancellationToken);
        }

        async Task TimeoutReceived(MessageContext context, CancellationToken cancellationtoken)
        {
            if (!context.Headers.TryGetValue(MsmqUtilities.PropertyHeaderPrefix + MsmqMessageDispatcher.TimeoutDestination, out var destination))
            {
                throw new Exception("This message does not represent a timeout");
            }

            if (!context.Headers.TryGetValue(MsmqUtilities.PropertyHeaderPrefix + MsmqMessageDispatcher.TimeoutAt, out var atString))
            {
                throw new Exception("This message does not represent a timeout");
            }

            var id = context.NativeMessageId; //Use native message ID as a key in the delayed delivery table
            var at = DateTimeOffsetHelper.ToDateTimeOffset(atString);

            var message = context.Extensions.Get<System.Messaging.Message>();

            var diff = DateTime.UtcNow - at;

            if (diff.Ticks > 0) // Due
            {
                await dispatcher.DispatchDelayedMessage(id, message.Extension, context.Body, destination, context.TransportTransaction).ConfigureAwait(false);
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

                try
                {
                    await storage.BeginTransaction(context.TransportTransaction).ConfigureAwait(false);
                    await storage.Store(timeout, context.TransportTransaction).ConfigureAwait(false);
                    await storage.CommitTransaction(context.TransportTransaction).ConfigureAwait(false);

                    storeCircuitBreaker.Success();
                }
                catch (Exception e)
                {
                    await storeCircuitBreaker.Failure(e).ConfigureAwait(false);
                    throw new Exception("Error while storing delayed message", e);
                }
                finally
                {
                    await storage.DisposeTransaction(context.TransportTransaction).ConfigureAwait(false);
                }

                poller.Signal(timeout.Time);
            }
        }

        async Task<ErrorHandleResult> OnError(ErrorContext errorContext, CancellationToken cancellationToken)
        {
            Log.Error($"OnError {errorContext.Message.MessageId}", errorContext.Exception);

            if (errorContext.ImmediateProcessingFailures < numberOfRetries)
            {
                return ErrorHandleResult.RetryRequired;
            }

            var message = errorContext.Message;

            ExceptionHeaderHelper.SetExceptionHeaders(message.Headers, errorContext.Exception);

            foreach (var pair in faultMetadata)
            {
                message.Headers[pair.Key] = pair.Value;
            }

            var outgoingMessage = new OutgoingMessage(message.NativeMessageId, message.Headers, message.Body);
            var transportOperation = new TransportOperation(outgoingMessage, new UnicastAddressTag(errorQueue));
            await dispatcher.Dispatch(new TransportOperations(transportOperation), errorContext.TransportTransaction, cancellationToken).ConfigureAwait(false);

            return ErrorHandleResult.Handled;
        }
    }
}