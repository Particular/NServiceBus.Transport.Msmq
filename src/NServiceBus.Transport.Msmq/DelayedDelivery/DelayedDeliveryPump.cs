namespace NServiceBus.Transport.Msmq.DelayedDelivery
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using System.Transactions;
    using Logging;
    using NServiceBus.Extensibility;
    using Routing;

    class DelayedDeliveryPump
    {
        public DelayedDeliveryPump(MsmqMessageDispatcher dispatcher,
                                   DueDelayedMessagePoller poller,
                                   IDelayedMessageStore storage,
                                   MessagePump messagePump,
                                   string errorQueue,
                                   int numberOfRetries,
                                   Action<string, Exception> criticalErrorAction,
                                   CriticalError criticalError,
                                   TimeSpan timeToWaitForStoreCircuitBreaker,
                                   Dictionary<string, string> faultMetadata,
                                   TransportTransactionMode transportTransactionMode)
        {
            this.dispatcher = dispatcher;
            this.poller = poller;
            this.storage = storage;
            this.numberOfRetries = numberOfRetries;
            this.faultMetadata = faultMetadata;
            pump = messagePump;
            this.errorQueue = errorQueue;
            this.criticalError = criticalError;

            txOption = transportTransactionMode == TransportTransactionMode.TransactionScope
                ? TransactionScopeOption.Required
                : TransactionScopeOption.RequiresNew;

            storeCircuitBreaker = new RepeatedFailuresOverTimeCircuitBreaker("DelayedDeliveryStore", timeToWaitForStoreCircuitBreaker,
                ex => criticalErrorAction("Failed to store delayed message", ex));
        }

        public async Task Start()
        {
            await pump.Init(TimeoutReceived, OnError, criticalError, null).ConfigureAwait(false);
            pump.Start(PushRuntimeSettings.Default);
        }

        public Task Stop()
        {
            return pump.Stop();
        }

        async Task TimeoutReceived(MessageContext context)
        {
            if (!context.Headers.TryGetValue(MsmqUtilities.PropertyHeaderPrefix + MsmqMessageDispatcher.TimeoutDestination, out var destination))
            {
                throw new Exception("This message does not represent a timeout");
            }

            if (!context.Headers.TryGetValue(MsmqUtilities.PropertyHeaderPrefix + MsmqMessageDispatcher.TimeoutAt, out var atString))
            {
                throw new Exception("This message does not represent a timeout");
            }

            var id = context.MessageId; //Use message ID as a key in the delayed delivery table
            var at = DateTimeOffsetHelper.ToDateTimeOffset(atString);

            var message = context.Extensions.Get<System.Messaging.Message>();

            var diff = DateTime.UtcNow - at;

            if (diff.Ticks > 0) // Due
            {
                dispatcher.DispatchDelayedMessage(id, message.Extension, context.Body, destination, context.TransportTransaction, new ContextBag());
            }
            else
            {
                var timeout = new DelayedMessage
                {
                    Destination = destination,
                    MessageId = id,
                    Body = context.Body,
                    Time = at.UtcDateTime,
                    Headers = message.Extension
                };

                try
                {
                    using (var tx = new TransactionScope(txOption, transactionOptions, TransactionScopeAsyncFlowOption.Enabled))
                    {
                        await storage.Store(timeout).ConfigureAwait(false);
                        tx.Complete();
                    }

                    storeCircuitBreaker.Success();
                }
                catch (OperationCanceledException)
                {
                    //Shutting down
                    return;
                }
                catch (Exception e)
                {
                    await storeCircuitBreaker.Failure(e).ConfigureAwait(false);
                    throw new Exception("Error while storing delayed message", e);
                }

                poller.Signal(timeout.Time);
            }
        }

        async Task<ErrorHandleResult> OnError(ErrorContext errorContext)
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

            var outgoingMessage = new OutgoingMessage(message.MessageId, message.Headers, message.Body);
            var transportOperation = new TransportOperation(outgoingMessage, new UnicastAddressTag(errorQueue));
            await dispatcher.Dispatch(new TransportOperations(transportOperation), errorContext.TransportTransaction, new ContextBag()).ConfigureAwait(false);

            return ErrorHandleResult.Handled;
        }

        readonly MsmqMessageDispatcher dispatcher;
        readonly DueDelayedMessagePoller poller;
        readonly IDelayedMessageStore storage;
        readonly int numberOfRetries;
        readonly MessagePump pump;
        readonly Dictionary<string, string> faultMetadata;
        readonly string errorQueue;
        readonly CriticalError criticalError;
        RepeatedFailuresOverTimeCircuitBreaker storeCircuitBreaker;
        readonly TransactionScopeOption txOption;
        readonly TransactionOptions transactionOptions = new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted };

        static readonly ILog Log = LogManager.GetLogger<DelayedDeliveryPump>();
    }
}