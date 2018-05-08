namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Collections.Generic;
    using System.Messaging;
    using Settings;


    class MsmqSettings
    {
        public MsmqSettings(ReadOnlySettings settings)
        {
            ExecuteInstaller = true;
            UseDeadLetterQueue = true;
            UseConnectionCache = true;
            UseTransactionalQueues = true;
            UseJournalQueue = false;
            UseDeadLetterQueueForMessagesWithTimeToBeReceived = false;
            TimeToReachQueue = Message.InfiniteTimeout;
            ScopeOptions = new MsmqScopeOptions();
            LabelGenerator = (headers => string.Empty);

            if (settings == null)
            {
                return;
            }

            if (settings.TryGet<bool>("ExecuteInstaller", out var executeInstaller))
            {
                ExecuteInstaller = executeInstaller;
            }

            if (settings.TryGet<bool>("UseDeadLetterQueue", out var useDeadLetterQueue))
            {
                UseDeadLetterQueue = useDeadLetterQueue;
            }

            if (settings.TryGet<bool>("UseConnectionCache", out var useConnectionCache))
            {
                UseConnectionCache = useConnectionCache;
            }

            if (settings.TryGet<bool>("UseTransactionalQueues", out var useTransactionalQueues))
            {
                UseTransactionalQueues = useTransactionalQueues;
            }

            if (settings.TryGet<TimeSpan>("TimeToReachQueue", out var timeToReachQueue))
            {
                TimeToReachQueue = timeToReachQueue;
            }

            if (settings.TryGet<bool>("UseDeadLetterQueueForMessagesWithTimeToBeReceived", out var useDeadLetterQueueForMessagesWithTimeToBeReceived))
            {
                UseDeadLetterQueueForMessagesWithTimeToBeReceived = useDeadLetterQueueForMessagesWithTimeToBeReceived;
            }

            if (settings.TryGet<MsmqScopeOptions>(out var options))
            {
                ScopeOptions = options;
            }

            if (settings.TryGet<Func<IReadOnlyDictionary<string, string>, string>>("msmqLabelGenerator", out var generator))
            {
                LabelGenerator = generator;
            }
        }

        public bool UseDeadLetterQueue { get; set; }

        public bool UseJournalQueue { get; set; }

        public bool UseConnectionCache { get; set; }

        public bool UseTransactionalQueues { get; set; }

        public TimeSpan TimeToReachQueue { get; set; }

        public bool UseDeadLetterQueueForMessagesWithTimeToBeReceived { get; set; }

        public bool ExecuteInstaller { get; set; }

        public MsmqScopeOptions ScopeOptions { get; set; }

        public Func<IReadOnlyDictionary<string, string>, string> LabelGenerator { get; set; }
    }
}