namespace NServiceBus
{
    /// <summary>
    ///
    /// </summary>
    public class DelayedDeliverySettings
    {
        /// <summary>
        ///
        /// </summary>
        public DelayedDeliverySettings()
        {
            NrOfRetries = 10;
        }

        /// <summary>
        ///
        /// </summary>
        public string TimeoutsQueueAddress { get; set; }

        /// <summary>
        ///
        /// </summary>
        public string SendOnlyErrorQueueAddress { get; set; }

        /// <summary>
        ///
        /// </summary>
        public int NrOfRetries { get; set; }
    }
}