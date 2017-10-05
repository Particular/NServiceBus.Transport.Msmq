namespace NServiceBus.Transport.Msmq
{
    using System;
    using ConsistencyGuarantees;
    using Features;
    using Settings;
    using Transport;

    class MsmqTimeToBeReceivedOverrideCheck
    {
        public MsmqTimeToBeReceivedOverrideCheck(ReadOnlySettings settings)
        {
            this.settings = settings;
        }

        public StartupCheckResult CheckTimeToBeReceivedOverrides()
        {
            var isTransactional = settings.GetRequiredTransactionModeForReceives() != TransportTransactionMode.None;
            var outBoxRunning = settings.IsFeatureActive(typeof(Outbox));

            settings.TryGetAuditMessageExpiration(out var auditMessageExpiration);
            var auditTTBROverridden = auditMessageExpiration > TimeSpan.Zero;

            return TimeToBeReceivedOverrideChecker.Check(isTransactional, outBoxRunning, auditTTBROverridden);
        }

        ReadOnlySettings settings;
    }
}