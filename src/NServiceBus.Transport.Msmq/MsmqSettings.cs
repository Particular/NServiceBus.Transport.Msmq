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
            ScopeOptions = new MsmqScopeOptions();

            if (settings.TryGet<MsmqScopeOptions>(out var options))
            {
                ScopeOptions = options;
            }

        }

        public bool UseDeadLetterQueue { get; set; }

        public bool UseJournalQueue { get; set; }

        public bool UseConnectionCache { get; set; }

        public bool UseTransactionalQueues { get; set; }

        public TimeSpan TimeToReachQueue { get; set; }

        public bool UseDeadLetterQueueForMessagesWithTimeToBeReceived { get; set; }

        public MsmqScopeOptions ScopeOptions { get; set; }

        public Func<IReadOnlyDictionary<string, string>, string> LabelGenerator { get; set; }

        public TimeSpan MessageEnumeratorTimeout { get; set; }

        public bool DisableNativeTtbrInTransactions { get; set; }

        public bool IgnoreIncomingTimeToBeReceivedHeaders { get; set; }
    }
}