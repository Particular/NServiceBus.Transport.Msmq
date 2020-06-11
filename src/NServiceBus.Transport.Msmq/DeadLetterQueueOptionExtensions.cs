namespace NServiceBus
{
    using Extensibility;
    using Transport.Msmq;

    /// <summary>
    /// Gives users fine grained control over routing via extension methods.
    /// </summary>
    public static class DeadLetterQueueOptionExtensions
    {
        internal const string KeyDeadLetterQueue = "MSMQ.UseDeadLetterQueue";

        /// <summary>
        /// Allows a to enable or disable MSMQ journaling.
        /// </summary>
        /// <param name="options">Option being extended.</param>
        /// <param name="enable">Either enable or disabling message journaling.</param>
        public static void UseDeadLetterQueue(this SendOptions options, bool enable)
        {
            Guard.AgainstNull(nameof(options), options);
            var ext = options.GetExtensions();
            ext.Set(KeyDeadLetterQueue, enable);
        }

        /// <summary>
        /// Allows a to enable or disable MSMQ journaling.
        /// </summary>
        /// <param name="options">Option being extended.</param>
        /// <param name="enable">Either enable or disabling message journaling.</param>
        public static void UseDeadLetterQueue(this ReplyOptions options, bool enable)
        {
            Guard.AgainstNull(nameof(options), options);
            var ext = options.GetExtensions();
            ext.Set(KeyDeadLetterQueue, enable);
        }

        /// <summary>
        /// Allows a to enable or disable MSMQ journaling.
        /// </summary>
        /// <param name="options">Option being extended.</param>
        /// <param name="enable">Either enable or disabling message journaling.</param>
        public static void UseDeadLetterQueue(this PublishOptions options, bool enable)
        {
            Guard.AgainstNull(nameof(options), options);
            var ext = options.GetExtensions();
            ext.Set(KeyDeadLetterQueue, enable);
        }
    }
}
