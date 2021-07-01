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
        public IDelayedMessageStore DelayedMessageStore { get; }

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
        public DelayedDeliverySettings(IDelayedMessageStore delayedMessageStore, int retries = 10, TimeSpan? timeToTriggerStoreCircuitBreaker = null)
        {
            Guard.AgainstNull(nameof(delayedMessageStore), delayedMessageStore);
            NumberOfRetries = retries;
            DelayedMessageStore = delayedMessageStore;
            if (timeToTriggerStoreCircuitBreaker.HasValue)
            {
                this.timeToTriggerStoreCircuitBreaker = timeToTriggerStoreCircuitBreaker.Value;
            }
        }
    }
}