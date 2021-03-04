namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Messaging;
    using System.Threading;
    using System.Threading.Tasks;
    using Logging;
    using NServiceBus.Extensibility;
    using Support;
    using Transport;

    class MessagePump : IMessageReceiver, IDisposable
    {
        public MessagePump(
            TimeSpan messageEnumeratorTimeout,
            Action<string, Exception, CancellationToken> criticalErrorAction,
            MsmqTransport transportSettings,
            ReceiveSettings receiveSettings)
        {
            Id = receiveSettings.Id;
            this.messageEnumeratorTimeout = messageEnumeratorTimeout;
            this.criticalErrorAction = criticalErrorAction;
            this.transportSettings = transportSettings;
            this.receiveSettings = receiveSettings;
        }

        public void Dispose()
        {
            peekCircuitBreaker?.Dispose();
            receiveCircuitBreaker?.Dispose();
            messagePumpCancellationTokenSource?.Dispose();
        }

        public Task Initialize(PushRuntimeSettings limitations, OnMessage onMessage, OnError onError, OnReceiveCompleted onReceiveCompleted, CancellationToken cancellationToken)
        {
            this.onMessage = onMessage;
            this.onError = onError;
            this.onReceiveCompleted = onReceiveCompleted;

            peekCircuitBreaker = new RepeatedFailuresOverTimeCircuitBreaker("MsmqPeek", TimeSpan.FromSeconds(30),
                ex => criticalErrorAction("Failed to peek " + receiveSettings.ReceiveAddress, ex, CancellationToken.None));
            receiveCircuitBreaker = new RepeatedFailuresOverTimeCircuitBreaker("MsmqReceive", TimeSpan.FromSeconds(30),
                ex => criticalErrorAction("Failed to receive from " + receiveSettings.ReceiveAddress, ex, CancellationToken.None));

            var inputAddress = MsmqAddress.Parse(receiveSettings.ReceiveAddress);
            var errorAddress = MsmqAddress.Parse(receiveSettings.ErrorQueue);

            if (!string.Equals(inputAddress.Machine, RuntimeEnvironment.MachineName,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(
                    $"MSMQ Dequeuing can only run against the local machine. Invalid inputQueue name '{receiveSettings.ReceiveAddress}'.");
            }

            inputQueue = new MessageQueue(inputAddress.FullPath, false, true, QueueAccessMode.Receive);
            errorQueue = new MessageQueue(errorAddress.FullPath, false, true, QueueAccessMode.Send);

            if (transportSettings.TransportTransactionMode != TransportTransactionMode.None && !QueueIsTransactional())
            {
                throw new ArgumentException(
                    $"Queue must be transactional if you configure the endpoint to be transactional ({receiveSettings.ReceiveAddress}).");
            }

            inputQueue.MessageReadPropertyFilter = DefaultReadPropertyFilter;

            if (receiveSettings.PurgeOnStartup)
            {
                inputQueue.Purge();
            }

            maxConcurrency = limitations.MaxConcurrency;
            concurrencyLimiter = new SemaphoreSlim(limitations.MaxConcurrency, limitations.MaxConcurrency);

            failureInfoStorage = new MsmqFailureInfoStorage(1000);

            return TaskEx.CompletedTask;
        }

        public Task StartReceive(CancellationToken cancellationToken)
        {
            MessageQueue.ClearConnectionCache();

            messagePumpCancellationTokenSource = new CancellationTokenSource();
            messageProcessingCancellationTokenSource = new CancellationTokenSource();

            // LongRunning is useless combined with async/await
            messagePumpTask = Task.Run(() => ProcessMessages(messagePumpCancellationTokenSource.Token), messagePumpCancellationTokenSource.Token);

            return Task.CompletedTask;
        }

        public async Task StopReceive(CancellationToken cancellationToken)
        {
            messagePumpCancellationTokenSource?.Cancel();

            cancellationToken.Register(() => messageProcessingCancellationTokenSource?.Cancel());

            await messagePumpTask.ConfigureAwait(false);

            while (concurrencyLimiter.CurrentCount != maxConcurrency)
            {
                // We are deliberately not forwarding the cancellation token here because
                // this loop is our way of waiting for all pending messaging operations
                // to participate in cooperative cancellation or not.
                // We do not want to rudely abort them because the cancellation token has been cancelled.
                // This allows us to preserve the same behaviour in v8 as in v7 in that,
                // if CancellationToken.None is passed to this method,
                // the method will only return when all in flight messages have been processed.
                // If, on the other hand, a non-default CancellationToken is passed,
                // all message processing operations have the opportunity to
                // participate in cooperative cancellation.
                // If we ever require a method of stopping the endpoint such that
                // all message processing is cancelled immediately,
                // we can provide that as a separate feature.
                await Task.Delay(50, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            concurrencyLimiter.Dispose();
            inputQueue.Dispose();
            errorQueue.Dispose();
        }

        [DebuggerNonUserCode]
        async Task ProcessMessages(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await InnerProcessMessages(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // For graceful shutdown purposes
                }
                catch (Exception ex)
                {
                    Logger.Error("MSMQ Message pump failed", ex);
                    await peekCircuitBreaker.Failure(ex).ConfigureAwait(false);
                }
            }
        }

        async Task InnerProcessMessages(CancellationToken cancellationToken)
        {
            using (var enumerator = inputQueue.GetMessageEnumerator2())
            {
                while (!messagePumpCancellationTokenSource.IsCancellationRequested)
                {
                    try
                    {
                        //note: .Peek will throw an ex if no message is available. It also turns out that .MoveNext is faster since message isn't read
                        if (!enumerator.MoveNext(messageEnumeratorTimeout))
                        {
                            continue;
                        }

                        peekCircuitBreaker.Success();
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("MSMQ receive operation failed", ex);
                        await peekCircuitBreaker.Failure(ex).ConfigureAwait(false);
                        continue;
                    }

                    if (messagePumpCancellationTokenSource.IsCancellationRequested)
                    {
                        return;
                    }

                    await concurrencyLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

                    _ = Task.Run(async () =>
                    {
                        var startedAt = DateTimeOffset.UtcNow;
                        Dictionary<string, string> headers = null;
                        string nativeMessageId = null;
                        ReceiveResult receiveResult;
                        var processingContext = new ContextBag();

                        try
                        {
                            (nativeMessageId, headers, receiveResult) = await ReceiveMessage(processingContext, messageProcessingCancellationTokenSource.Token).ConfigureAwait(false);

                            if (string.IsNullOrEmpty(nativeMessageId))
                            {
                                //no message found to process
                                return;
                            }

                            var receiveCompletedContext = new ReceiveCompletedContext(nativeMessageId, receiveResult, headers, startedAt, DateTimeOffset.UtcNow, processingContext);
                            await onReceiveCompleted(receiveCompletedContext, cancellationToken).ConfigureAwait(false);

                            receiveCircuitBreaker.Success();
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn("MSMQ receive operation failed", ex);
                            await receiveCircuitBreaker.Failure(ex).ConfigureAwait(false);
                        }
                        finally
                        {
                            concurrencyLimiter.Release();
                        }
                    }, cancellationToken);
                }
            }
        }

        async Task<(string, Dictionary<string, string>, ReceiveResult)> ReceiveMessage(ContextBag context, CancellationToken cancellationToken)
        {
            var transportTransaction = new TransportTransaction();

            using (var transaction = CreateReceiveTransaction(transportTransaction))
            {
                transportTransaction.Set(transaction);

                if (!TryReceiveMessage(transaction, out var message))
                {
                    return (null, null, default);
                }

                if (!TryExtractHeaders(message, out var headers))
                {
                    var error = $"Message '{message.Id}' is classified as a poison message and will be moved to the configured error queue.";

                    Logger.Error(error);

                    transaction.SendMessage(errorQueue, message);

                    transaction.Commit();
                    return (message.Id, new Dictionary<string, string>(), ReceiveResult.MovedToErrorQueue);
                }

                if (!transportSettings.IgnoreIncomingTimeToBeReceivedHeaders && TimeToBeReceived.HasElapsed(headers))
                {
                    Logger.Debug($"Discarding message {message.Id} due to lapsed Time To Be Received header");
                    return (message.Id, headers, ReceiveResult.Expired);
                }

                var body = await ReadStream(message.BodyStream).ConfigureAwait(false);

                if (transaction.RollbackBeforeErrorHandlingRequired)
                {
                    if (failureInfoStorage.TryGetFailureInfoForMessage(message.Id, out var failureInfo))
                    {
                        var errorContext = new ErrorContext(failureInfo.Exception, headers, message.Id, body, transportTransaction, failureInfo.NumberOfProcessingAttempts, context);

                        var errorHandleResult = await InvokeOnError(errorContext, cancellationToken).ConfigureAwait(false);

                        if (errorHandleResult != ReceiveResult.RetryRequired)
                        {
                            transaction.Commit();
                            failureInfoStorage.ClearFailureInfoForMessage(message.Id);
                            return (message.Id, headers, errorHandleResult);
                        }

                        message.BodyStream.Position = 0;

                        // we re-extract headers and body since they might have been changed during error handling
                        headers = MsmqUtilities.ExtractHeaders(message);
                        body = await ReadStream(message.BodyStream).ConfigureAwait(false);
                    }
                }

                var messageContext = new MessageContext(message.Id, headers, body, transportTransaction, context);

                try
                {
                    await onMessage(messageContext, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
                {
                    Logger.Info("Message processing cancelled. Rolling back transaction.", ex);
                    transaction.Rollback();
                    return (message.Id, headers, ReceiveResult.RetryRequired);
                }
                catch (Exception ex)
                {
                    if (transaction.RollbackBeforeErrorHandlingRequired)
                    {
                        failureInfoStorage.RecordFailureInfoForMessage(message.Id, ex);

                        transaction.Rollback();

                        return (null, null, default);
                    }

                    message.BodyStream.Position = 0;

                    // we re-extract headers and body since they might have been changed during the failed processing attempt
                    var errorHeaders = MsmqUtilities.ExtractHeaders(message);
                    var errorBody = await ReadStream(message.BodyStream).ConfigureAwait(false);

                    var errorContext = new ErrorContext(ex, errorHeaders, message.Id, errorBody, transportTransaction, 1, context);

                    var onErrorResult = await InvokeOnError(errorContext, cancellationToken).ConfigureAwait(false);

                    if (onErrorResult == ReceiveResult.RetryRequired)
                    {
                        transaction.Rollback();

                        return (message.Id, headers, ReceiveResult.RetryRequired);
                    }
                }

                transaction.Commit();

                if (transaction.RollbackBeforeErrorHandlingRequired)
                {
                    failureInfoStorage.ClearFailureInfoForMessage(message.Id);
                }

                return (message.Id, headers, ReceiveResult.Succeeded);
            }
        }

        IMsmqTransaction CreateReceiveTransaction(TransportTransaction transportTransaction)
        {
            switch (transportSettings.TransportTransactionMode)
            {
                case TransportTransactionMode.None:
                    return new NoTransaction();
                case TransportTransactionMode.ReceiveOnly:
                    return new NativeMsmqTransaction(false, transportTransaction);
                case TransportTransactionMode.SendsAtomicWithReceive:
                    return new NativeMsmqTransaction(true, transportTransaction);
                case TransportTransactionMode.TransactionScope:
                    return new TransactionScopeTransaction(transportTransaction, transportSettings.TransactionScopeOptions.TransactionOptions);
                default:
                    throw new InvalidOperationException($"Unknown transaction mode {transportSettings.TransportTransactionMode}");
            }
        }

        async Task<ReceiveResult> InvokeOnError(ErrorContext errorContext, CancellationToken cancellationToken)
        {
            try
            {
                return await onError(errorContext, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return ReceiveResult.RetryRequired;
            }
            catch (Exception ex)
            {
                criticalErrorAction($"Failed to execute recoverability policy for message with native ID: `{errorContext.Message.NativeMessageId}`", ex, CancellationToken.None);

                return ReceiveResult.RetryRequired;
            }
        }

        bool TryReceiveMessage(IMsmqTransaction transaction, out Message message)
        {
            try
            {
                message = transaction.Receive(inputQueue, TimeSpan.FromMilliseconds(10));

                return true;
            }
            catch (MessageQueueException ex)
            {
                if (ex.MessageQueueErrorCode == MessageQueueErrorCode.IOTimeout)
                {
                    //We should only get an IOTimeout exception here if another process removed the message between us peeking and now.
                    message = null;
                    return false;
                }
                throw;
            }
        }

        bool QueueIsTransactional()
        {
            try
            {
                return inputQueue.Transactional;
            }
            catch (MessageQueueException msmqEx)
            {
                var error =
                    $"There is a problem with the input inputQueue: {inputQueue.Path}. See the enclosed exception for details.";
                if (msmqEx.MessageQueueErrorCode == MessageQueueErrorCode.QueueNotFound)
                {
                    error =
                        $"The queue {inputQueue.Path} does not exist. Run the CreateQueues.ps1 script included in the project output, or enable queue creation on startup using EndpointConfiguration.EnableInstallers().";
                }

                if (msmqEx.MessageQueueErrorCode == MessageQueueErrorCode.AccessDenied)
                {
                    error =
                        $"Access denied for the queue {inputQueue.Path}. Ensure the user has Get Properties permission on the queue.";
                }

                throw new Exception(error, msmqEx);
            }
            catch (Exception ex)
            {
                var error =
                    $"There is a problem with the input inputQueue: {inputQueue.Path}. See the enclosed exception for details.";
                throw new Exception(error, ex);
            }
        }

        bool TryExtractHeaders(Message message, out Dictionary<string, string> headers)
        {
            try
            {
                headers = MsmqUtilities.ExtractHeaders(message);
                return true;
            }
            catch (Exception ex)
            {
                var error = $"Message '{message.Id}' has corrupted headers";

                Logger.Warn(error, ex);

                headers = null;
                return false;
            }
        }

        static async Task<byte[]> ReadStream(Stream bodyStream)
        {
            bodyStream.Seek(0, SeekOrigin.Begin);
            var length = (int)bodyStream.Length;
            var body = new byte[length];
            await bodyStream.ReadAsync(body, 0, length).ConfigureAwait(false);
            return body;
        }

        public ISubscriptionManager Subscriptions => null;

        public string Id { get; }

        CancellationTokenSource messagePumpCancellationTokenSource;
        CancellationTokenSource messageProcessingCancellationTokenSource;
        int maxConcurrency;
        SemaphoreSlim concurrencyLimiter;
        MessageQueue errorQueue;
        MessageQueue inputQueue;

        OnMessage onMessage;
        OnError onError;
        OnReceiveCompleted onReceiveCompleted;

        Task messagePumpTask;

        RepeatedFailuresOverTimeCircuitBreaker peekCircuitBreaker;
        RepeatedFailuresOverTimeCircuitBreaker receiveCircuitBreaker;
        TimeSpan messageEnumeratorTimeout;
        readonly Action<string, Exception, CancellationToken> criticalErrorAction;
        readonly MsmqTransport transportSettings;
        readonly ReceiveSettings receiveSettings;
        MsmqFailureInfoStorage failureInfoStorage;

        static ILog Logger = LogManager.GetLogger<MessagePump>();

        static MessagePropertyFilter DefaultReadPropertyFilter = new MessagePropertyFilter
        {
            Body = true,
            TimeToBeReceived = true,
            Recoverable = true,
            Id = true,
            ResponseQueue = true,
            CorrelationId = true,
            Extension = true,
            AppSpecific = true
        };
    }
}