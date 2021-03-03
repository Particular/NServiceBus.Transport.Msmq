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
            cancellationTokenSource?.Dispose();
        }

        public Task Initialize(PushRuntimeSettings limitations, OnMessage onMessage, OnError onError, CancellationToken cancellationToken)
        {
            this.onMessage = onMessage;
            this.onError = onError;

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

            cancellationTokenSource = new CancellationTokenSource();
            this.cancellationToken = cancellationTokenSource.Token;

            // LongRunning is useless combined with async/await
            messagePumpTask = Task.Run(() => ProcessMessages(), CancellationToken.None);

            return Task.CompletedTask;
        }

        public async Task StopReceive(CancellationToken cancellationToken)
        {
            cancellationTokenSource.Cancel();

            await messagePumpTask.ConfigureAwait(false);

            try
            {
                using (var shutdownCancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    while (concurrencyLimiter.CurrentCount != maxConcurrency)
                    {
                        await Task.Delay(50, shutdownCancellationTokenSource.Token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Error("The message pump failed to stop with in the time allowed(30s)");
            }

            concurrencyLimiter.Dispose();
            inputQueue.Dispose();
            errorQueue.Dispose();
        }

        [DebuggerNonUserCode]
        async Task ProcessMessages()
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await InnerProcessMessages().ConfigureAwait(false);
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

        async Task InnerProcessMessages()
        {
            using (var enumerator = inputQueue.GetMessageEnumerator2())
            {
                while (!cancellationTokenSource.IsCancellationRequested)
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

                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        return;
                    }

                    await concurrencyLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

                    _ = ReceiveMessage();
                }
            }
        }

        Task ReceiveMessage()
        {
            return TaskEx.Run(async state =>
            {
                var messagePump = (MessagePump)state;

                try
                {
                    var transportTransaction = new TransportTransaction();

                    using (var transaction = CreateReceiveTransaction(transportTransaction))
                    {
                        transportTransaction.Set(transaction);

                        if (!TryReceiveMessage(transaction, out var message))
                        {
                            return;
                        }

                        if (!TryExtractHeaders(message, out var headers))
                        {
                            var error = $"Message '{message.Id}' is classified as a poison message and will be moved to the configured error queue.";

                            Logger.Error(error);

                            transaction.SendMessage(errorQueue, message);

                            transaction.Commit();
                            return;
                        }

                        if (!transportSettings.IgnoreIncomingTimeToBeReceivedHeaders && TimeToBeReceived.HasElapsed(headers))
                        {
                            Logger.Debug($"Discarding message {message.Id} due to lapsed Time To Be Received header");
                            return;
                        }

                        var body = await ReadStream(message.BodyStream).ConfigureAwait(false);

                        if (transaction.RollbackBeforeErrorHandlingRequired)
                        {

                            if (failureInfoStorage.TryGetFailureInfoForMessage(message.Id, out var failureInfo))
                            {
                                var errorContext = new ErrorContext(failureInfo.Exception, headers, message.Id, body, transportTransaction, failureInfo.NumberOfProcessingAttempts);

                                var errorHandleResult = await onError(errorContext, CancellationToken.None).ConfigureAwait(false);

                                if (errorHandleResult == ErrorHandleResult.Handled)
                                {
                                    transaction.Commit();
                                }
                                else
                                {
                                    transaction.Rollback();
                                }

                                return;
                            }
                        }

                        var messageContext = new MessageContext(message.Id, headers, body, transportTransaction, new ContextBag());

                        try
                        {
                            await onMessage(messageContext, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            if (transaction.RollbackBeforeErrorHandlingRequired)
                            {
                                failureInfoStorage.RecordFailureInfoForMessage(message.Id, ex);

                                transaction.Rollback();

                                return;
                            }

                            message.BodyStream.Position = 0;

                            // we re-extact headers and body since they might have been changed during the failed processing attempt
                            var errorHeaders = MsmqUtilities.ExtractHeaders(message);
                            var errorBody = await ReadStream(message.BodyStream).ConfigureAwait(false);

                            var errorContext = new ErrorContext(ex, headers, message.Id, body, transportTransaction, 1);

                            var onErrorResult = await onError(errorContext, CancellationToken.None).ConfigureAwait(false);

                            if (onErrorResult == ErrorHandleResult.RetryRequired)
                            {
                                transaction.Rollback();

                                return;
                            }
                        }

                        if (transaction.RollbackBeforeErrorHandlingRequired)
                        {
                            failureInfoStorage.ClearFailureInfoForMessage(message.Id);
                        }

                        transaction.Commit();

                        //onComplete
                    }

                    messagePump.receiveCircuitBreaker.Success();
                }
                catch (OperationCanceledException)
                {
                    // Intentionally ignored
                }
                catch (Exception ex)
                {
                    Logger.Warn("MSMQ receive operation failed", ex);
                    await messagePump.receiveCircuitBreaker.Failure(ex).ConfigureAwait(false);
                }
                finally
                {
                    messagePump.concurrencyLimiter.Release();
                }
            }, this);
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

        CancellationToken cancellationToken;
        CancellationTokenSource cancellationTokenSource;
        int maxConcurrency;
        SemaphoreSlim concurrencyLimiter;
        MessageQueue errorQueue;
        MessageQueue inputQueue;

        OnMessage onMessage;
        OnError onError;

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