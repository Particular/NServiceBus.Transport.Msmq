namespace NServiceBus
{
    using Transport.Msmq;

    /// <summary>
    ///
    /// </summary>
    public class DelayedDeliverySettings
    {
        /// <summary>
        ///
        /// </summary>
        public ITimeoutStorage TimeoutStorage { get; }

        /// <summary>
        ///
        /// </summary>
        public DelayedDeliverySettings(ITimeoutStorage timeoutStorage)
        {
            Guard.AgainstNull(nameof(timeoutStorage), timeoutStorage);
            TimeoutStorage = timeoutStorage;
            NrOfRetries = 10;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="queueName"></param>
        /// <returns></returns>
        public DelayedDeliverySettings TimeoutsQueue(string queueName)
        {
            Guard.AgainstNullAndEmpty(nameof(queueName), queueName);
            TimeoutsQueueAddress = MsmqAddress.Parse(queueName);
            return this;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="queueName"></param>
        /// <returns></returns>
        public DelayedDeliverySettings SendOnlyErrorQueue(string queueName)
        {
            Guard.AgainstNullAndEmpty(nameof(queueName), queueName);
            SendOnlyErrorQueueAddress = MsmqAddress.Parse(queueName);
            return this;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="retries"></param>
        /// <returns></returns>
        public DelayedDeliverySettings Retries(int retries)
        {
            NrOfRetries = retries;
            return this;
        }

        internal MsmqAddress TimeoutsQueueAddress { get; set; }

        internal MsmqAddress SendOnlyErrorQueueAddress { get; set; }

        internal MsmqAddress GetErrorQueueAddress() => SendOnlyErrorQueueAddress.IsEmpty() ? ErrorQueue : SendOnlyErrorQueueAddress;

        internal int NrOfRetries { get; set; }
        internal MsmqAddress ErrorQueue { get; set; }
    }
}