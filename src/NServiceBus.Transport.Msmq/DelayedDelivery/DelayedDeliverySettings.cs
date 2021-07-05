namespace NServiceBus
{
    using System;
    using Transport.Msmq;

    /// <summary>
    /// Configures delayed delivery support.
    /// </summary>
    public class DelayedDeliverySettings
    {
        int numberOfRetries;
        TimeSpan timeToTriggerStoreCircuitBreaker = TimeSpan.FromSeconds(30);
        TimeSpan timeToTriggerFetchCircuitBreaker = TimeSpan.FromSeconds(30);
        TimeSpan timeToTriggerDispatchCircuitBreaker = TimeSpan.FromSeconds(30);
        int maximumRecoveryFailuresPerSecond = 1;

        /// <summary>
        /// The store to keep delayed messages.
        /// </summary>
        public IDelayedMessageStore DelayedMessageStore { get; }

        /// <summary>
        /// Number of retries when trying to forward due delayed messages.
        /// </summary>
        public int NumberOfRetries
        {
            get => numberOfRetries;
            set
            {
                Guard.AgainstNegativeAndZero("value", value);
                numberOfRetries = value;
            }
        }

        /// <summary>
        /// Time to wait before triggering the circuit breaker that monitors the storing of delayed messages in the database. Defaults to 30 seconds.
        /// </summary>
        public TimeSpan TimeToTriggerStoreCircuitBreaker
        {
            get => timeToTriggerStoreCircuitBreaker;
            set
            {
                Guard.AgainstNegativeAndZero("value", value);
                timeToTriggerStoreCircuitBreaker = value;
            }
        }

        /// <summary>
        /// Time to wait before triggering the circuit breaker that monitors the fetching of due delayed messages from the database. Defaults to 30 seconds.
        /// </summary>
        public TimeSpan TimeToTriggerFetchCircuitBreaker
        {
            get => timeToTriggerFetchCircuitBreaker;
            set
            {
                Guard.AgainstNegativeAndZero("value", value);
                timeToTriggerFetchCircuitBreaker = value;
            }
        }

        /// <summary>
        /// Time to wait before triggering the circuit breaker that monitors the dispatching of due delayed messages to the destination. Defaults to 30 seconds.
        /// </summary>
        public TimeSpan TimeToTriggerDispatchCircuitBreaker
        {
            get => timeToTriggerDispatchCircuitBreaker;
            set
            {
                Guard.AgainstNegativeAndZero("value", value);
                timeToTriggerDispatchCircuitBreaker = value;
            }
        }

        /// <summary>
        /// Maximum number of recovery failures per second that triggers the recovery circuit breaker. Recovery attempts are attempts to increment the failure
        /// counter after a failed dispatch and forwarding messages to the error queue. Defaults to 1/s.
        /// </summary>
        public int MaximumRecoveryFailuresPerSecond
        {
            get => maximumRecoveryFailuresPerSecond;
            set
            {
                Guard.AgainstNegativeAndZero("value", value);
                maximumRecoveryFailuresPerSecond = value;
            }
        }

        /// <summary>
        /// Configures delayed delivery.
        /// </summary>
        /// <param name="delayedMessageStore">The store to keep delayed messages.</param>
        /// <param name="retries">Number of retries when trying to forward due delayed messages.</param>
        /// <param name="timeToTriggerStoreCircuitBreaker">Time to wait before triggering the circuit breaker that monitors the storing of delayed messages in the database. Defaults to 30 seconds.</param>
        /// <param name="timeToTriggerFetchCircuitBreaker">Time to wait before triggering the circuit breaker that monitors the fetching of due delayed messages from the database. Defaults to 30 seconds.</param>
        /// <param name="timeToTriggerDispatchCircuitBreaker">Time to wait before triggering the circuit breaker that monitors the dispatching of due delayed messages to the destination. Defaults to 30 seconds.</param>
        /// <param name="maximumRecoveryFailuresPerSecond">Maximum number of recovery failures per second that triggers the recovery circuit breaker. Defaults to 1/s.</param>
        public DelayedDeliverySettings(IDelayedMessageStore delayedMessageStore,
            int retries = 10,
            TimeSpan? timeToTriggerStoreCircuitBreaker = null,
            TimeSpan? timeToTriggerFetchCircuitBreaker = null,
            TimeSpan? timeToTriggerDispatchCircuitBreaker = null,
            int maximumRecoveryFailuresPerSecond = 1)
        {
            Guard.AgainstNull(nameof(delayedMessageStore), delayedMessageStore);
            NumberOfRetries = retries;
            DelayedMessageStore = delayedMessageStore;
            MaximumRecoveryFailuresPerSecond = maximumRecoveryFailuresPerSecond;

            if (timeToTriggerStoreCircuitBreaker.HasValue)
            {
                this.timeToTriggerStoreCircuitBreaker = timeToTriggerStoreCircuitBreaker.Value;
            }

            if (timeToTriggerFetchCircuitBreaker.HasValue)
            {
                this.timeToTriggerFetchCircuitBreaker = timeToTriggerFetchCircuitBreaker.Value;
            }

            if (timeToTriggerDispatchCircuitBreaker.HasValue)
            {
                this.timeToTriggerDispatchCircuitBreaker = timeToTriggerDispatchCircuitBreaker.Value;
            }
        }
    }
}