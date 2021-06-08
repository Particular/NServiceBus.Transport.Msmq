namespace NServiceBus
{
    using Transport.Msmq;

    /// <summary>
    ///
    /// </summary>
    public class DelayedDeliverySettings
    {
        int numberOfRetries;

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
        ///
        /// </summary>
        public DelayedDeliverySettings(ITimeoutStorage timeoutStorage, int retries = 10)
        {
            Guard.AgainstNull(nameof(timeoutStorage), timeoutStorage);
            NumberOfRetries = retries;
            TimeoutStorage = timeoutStorage;
        }
    }
}