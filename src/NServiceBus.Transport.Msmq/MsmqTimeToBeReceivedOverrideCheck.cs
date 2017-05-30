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
            var usingMsmq = settings.Get<TransportDefinition>() is MsmqTransport;
            var isTransactional = settings.GetRequiredTransactionModeForReceives() != TransportTransactionMode.None;
            var outBoxRunning = settings.IsFeatureActive(typeof(Outbox));


            //var messageAuditingConfig = settings.GetOrDefault<AuditConfigReader.Result>();
            //var auditTTBROverridden = messageAuditingConfig?.TimeToBeReceived > TimeSpan.Zero;

            //workaround for AuditConfigReader.Result not being accessible
            var auditTTBROverridden = false;
            var auditConfigReaderResult = "NServiceBus.AuditConfigReader+Result";

            if (settings.HasSetting(auditConfigReaderResult))
            {
                dynamic result = settings.Get(auditConfigReaderResult);
                auditTTBROverridden = result?.TimeToBeReceived > TimeSpan.Zero;
            }

            return TimeToBeReceivedOverrideChecker.Check(usingMsmq, isTransactional, outBoxRunning, auditTTBROverridden);
        }

        ReadOnlySettings settings;
    }
}