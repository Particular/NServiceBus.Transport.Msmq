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
            Action<string, Exception> criticalErrorAction,
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
            // Injected
        }

        public Task Initialize(PushRuntimeSettings limitations, Func<MessageContext, Task> onMessage,
            Func<ErrorContext, Task<ErrorHandleResult>> onError)
        {
            peekCircuitBreaker = new RepeatedFailuresOverTimeCircuitBreaker("MsmqPeek", TimeSpan.FromSeconds(30),
                ex => criticalErrorAction("Failed to peek " + receiveSettings.ReceiveAddress, ex));
            receiveCircuitBreaker = new RepeatedFailuresOverTimeCircuitBreaker("MsmqReceive", TimeSpan.FromSeconds(30),
                ex => criticalErrorAction("Failed to receive from " + receiveSettings.ReceiveAddress, ex));

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

            receiveStrategy = receiveStrategyFactory(transportSettings.TransportTransactionMode);
            receiveStrategy.Init(inputQueue, errorQueue, onMessage, onError, criticalErrorAction, transportSettings.IgnoreIncomingTimeToBeReceivedHeaders);

            maxConcurrency = limitations.MaxConcurrency;
            concurrencyLimiter = new SemaphoreSlim(limitations.MaxConcurrency, limitations.MaxConcurrency);
            return TaskEx.CompletedTask;
        }

        public Task StartReceive()
        {
            MessageQueue.ClearConnectionCache();

            if (receiveSettings.PurgeOnStartup)
            {
                inputQueue.Purge();
            }

            cancellationTokenSource = new CancellationTokenSource();
            cancellationToken = cancellationTokenSource.Token;

            // LongRunning is useless combined with async/await
            messagePumpTask = Task.Run(() => ProcessMessages(), CancellationToken.None);

            return Task.CompletedTask;
        }

        public async Task StopReceive()
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
                var messagePump = (MessagePump) state;

                try
                {
                    await messagePump.receiveStrategy.ReceiveMessage().ConfigureAwait(false);
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

        CancellationToken cancellationToken;
        CancellationTokenSource cancellationTokenSource;
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
        private readonly Action<string, Exception> criticalErrorAction;
        private readonly MsmqTransport transportSettings;
        private readonly ReceiveSettings receiveSettings;

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