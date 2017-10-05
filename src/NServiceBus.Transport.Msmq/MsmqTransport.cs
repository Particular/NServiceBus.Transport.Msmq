namespace NServiceBus
{
    using System;
    using System.Collections.Generic;
    using Features;
    using Routing;
    using Settings;
    using Transport;
    using Transport.Msmq;

    /// <summary>
    /// Transport definition for MSMQ.
    /// </summary>
    public class MsmqTransport : TransportDefinition, IMessageDrivenSubscriptionTransport
    {
        /// <summary>
        /// <see cref="TransportDefinition.ExampleConnectionStringForErrorMessage" />.
        /// </summary>
        public override string ExampleConnectionStringForErrorMessage => "cacheSendConnection=true;journal=false;deadLetter=true";

        /// <summary>
        /// <see cref="TransportDefinition.RequiresConnectionString" />.
        /// </summary>
        public override bool RequiresConnectionString => false;

        /// <summary>
        /// Initializes the transport infrastructure for msmq.
        /// </summary>
        /// <param name="settings">The settings.</param>
        /// <param name="connectionString">The connection string.</param>
        /// <returns>the transport infrastructure for msmq.</returns>
        public override TransportInfrastructure Initialize(SettingsHolder settings, string connectionString)
        {
            Guard.AgainstNull(nameof(settings), settings);

            if (!settings.GetOrDefault<bool>("Endpoint.SendOnly") && !settings.TryGetExplicitlyConfiguredErrorQueueAddress(out string _))
            {
                throw new Exception("Faults forwarding requires an error queue to be specified using 'EndpointConfiguration.SendFailedMessagesTo()'");
            }

            var msmqSettings = connectionString != null ? new MsmqConnectionStringBuilder(connectionString)
                .RetrieveSettings() : new MsmqSettings();

            msmqSettings.UseDeadLetterQueueForMessagesWithTimeToBeReceived = settings.GetOrDefault<bool>(UseDeadLetterQueueForMessagesWithTimeToBeReceived);
            if (settings.TryGet<bool>(ExecuteInstaller, out var executeInstaller))
            {
                msmqSettings.ExecuteInstaller = executeInstaller;
            }
            else
            {
                msmqSettings.ExecuteInstaller = true;
            }


            settings.Set<MsmqSettings>(msmqSettings);


            if (!settings.TryGet(out MsmqScopeOptions scopeOptions))
            {
                scopeOptions = new MsmqScopeOptions();
            }

            if (!settings.TryGet("msmqLabelGenerator", out Func<IReadOnlyDictionary<string, string>, string> messageLabelGenerator))
            {
                messageLabelGenerator = headers => string.Empty;
            }

            var isTransactional = IsTransactional(settings);
            var outBoxRunning = settings.IsFeatureActive(typeof(Features.Outbox));

            settings.TryGetAuditMessageExpiration(out var auditMessageExpiration);

            return new MsmqTransportInfrastructure(msmqSettings, settings.Get<QueueBindings>(), scopeOptions, messageLabelGenerator, isTransactional, outBoxRunning, auditMessageExpiration);
        }


        static bool IsTransactional(ReadOnlySettings settings)
        {
            //if user has asked for a explicit level infer IsTransactional from that setting
            if (settings.TryGet(out TransportTransactionMode requestedTransportTransactionMode))
            {
                return requestedTransportTransactionMode != TransportTransactionMode.None;
            }
            //otherwise use msmq default which is transactional
            return true;
        }

        internal const string UseDeadLetterQueueForMessagesWithTimeToBeReceived = "UseDeadLetterQueueForMessagesWithTimeToBeReceived";

        internal const string ExecuteInstaller = "ExecuteInstaller";
    }
}