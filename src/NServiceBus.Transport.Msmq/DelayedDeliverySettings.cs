namespace NServiceBus
{
    using System;
    using Transport.Msmq;

    /// <summary>
    ///
    /// </summary>
    public class DelayedDeliverySettings
    {
        int numberOfRetries;
        TimeSpan timeToTriggerStoreCircuitBreaker = TimeSpan.FromSeconds(30);

        /// <summary>
        ///
        /// </summary>
        public ITimeoutStorage TimeoutStorage { get; }

        /// <summary>
        /// Number of retries.
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
        /// Time to wait before triggering the circuit breaker that monitors the storing of delayed messages in the database.
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
        ///
        /// </summary>
        public DelayedDeliverySettings(ITimeoutStorage timeoutStorage, int retries = 10, TimeSpan? timeToTriggerStoreCircuitBreaker = null)
        {
            Guard.AgainstNull(nameof(timeoutStorage), timeoutStorage);
            NumberOfRetries = retries;
            TimeoutStorage = timeoutStorage;
            if (timeToTriggerStoreCircuitBreaker.HasValue)
            {
                this.timeToTriggerStoreCircuitBreaker = timeToTriggerStoreCircuitBreaker.Value;
            }
        }
    }
}