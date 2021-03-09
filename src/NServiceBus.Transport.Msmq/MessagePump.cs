namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Diagnostics;
    using System.Messaging;
    using System.Threading;
    using System.Threading.Tasks;
    using Logging;
    using Support;
    using Transport;

    class MessagePump : IMessageReceiver, IDisposable
    {
        public MessagePump(
            Func<TransportTransactionMode, ReceiveStrategy> receiveStrategyFactory,
            TimeSpan messageEnumeratorTimeout,
            Action<string, Exception, CancellationToken> criticalErrorAction,
            MsmqTransport transportSettings,
            ReceiveSettings receiveSettings)
        {
            Id = receiveSettings.Id;
            this.receiveStrategyFactory = receiveStrategyFactory;
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

        public Task Initialize(PushRuntimeSettings limitations, OnMessage onMessage, OnError onError, CancellationToken cancellationToken)
        {
            messagePumpCancellationTokenSource = new CancellationTokenSource();

            var tokenForCriticalErrorAction = messageProcessingCancellationTokenSource.Token; // Prevent ObjectDisposed after endpoint shut down by using a local variable

            peekCircuitBreaker = new RepeatedFailuresOverTimeCircuitBreaker("MsmqPeek", TimeSpan.FromSeconds(30),
                ex => criticalErrorAction("Failed to peek " + receiveSettings.ReceiveAddress, ex, tokenForCriticalErrorAction));
            receiveCircuitBreaker = new RepeatedFailuresOverTimeCircuitBreaker("MsmqReceive", TimeSpan.FromSeconds(30),
                ex => criticalErrorAction("Failed to receive from " + receiveSettings.ReceiveAddress, ex, tokenForCriticalErrorAction));

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

            receiveStrategy = receiveStrategyFactory(transportSettings.TransportTransactionMode);
            receiveStrategy.Init(inputQueue, errorQueue, onMessage, onError, criticalErrorAction, transportSettings.IgnoreIncomingTimeToBeReceivedHeaders);

            maxConcurrency = limitations.MaxConcurrency;
            concurrencyLimiter = new SemaphoreSlim(limitations.MaxConcurrency, limitations.MaxConcurrency);
            return TaskEx.CompletedTask;
        }

        public Task StartReceive(CancellationToken cancellationToken)
        {
            MessageQueue.ClearConnectionCache();

            messageProcessingCancellationTokenSource = new CancellationTokenSource();

            // LongRunning is useless combined with async/await
            messagePumpTask = Task.Run(() => PumpMessages(), cancellationToken);

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

            messagePumpCancellationTokenSource?.Dispose();
            messageProcessingCancellationTokenSource?.Dispose();
        }

        [DebuggerNonUserCode]
        async Task PumpMessages()
        {
            while (!messagePumpCancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    await InnerPumpMessages().ConfigureAwait(false);
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

        async Task InnerPumpMessages()
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

                    await concurrencyLimiter.WaitAsync(messagePumpCancellationTokenSource.Token).ConfigureAwait(false);

                    _ = ReceiveMessage();
                }
            }
        }

        Task ReceiveMessage()
        {
            return Task.Factory.StartNew(async _ =>
            {
                try
                {
                    await receiveStrategy.ReceiveMessage(messageProcessingCancellationTokenSource.Token).ConfigureAwait(false);
                    receiveCircuitBreaker.Success();
                }
                catch (OperationCanceledException)
                {
                    // Intentionally ignored
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
            },
            null,
            CancellationToken.None,  // CancellationToken.None is used here since cancelling the task before it can run can cause the concurrencyLimiter to not be released
            TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default)
                .Unwrap();

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

        public ISubscriptionManager Subscriptions => null;

        public string Id { get; }

        CancellationTokenSource messagePumpCancellationTokenSource;
        CancellationTokenSource messageProcessingCancellationTokenSource;
        int maxConcurrency;
        SemaphoreSlim concurrencyLimiter;
        MessageQueue errorQueue;
        MessageQueue inputQueue;

        Task messagePumpTask;

        ReceiveStrategy receiveStrategy;

        RepeatedFailuresOverTimeCircuitBreaker peekCircuitBreaker;
        RepeatedFailuresOverTimeCircuitBreaker receiveCircuitBreaker;
        Func<TransportTransactionMode, ReceiveStrategy> receiveStrategyFactory;
        TimeSpan messageEnumeratorTimeout;
        readonly Action<string, Exception, CancellationToken> criticalErrorAction;
        readonly MsmqTransport transportSettings;
        readonly ReceiveSettings receiveSettings;

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