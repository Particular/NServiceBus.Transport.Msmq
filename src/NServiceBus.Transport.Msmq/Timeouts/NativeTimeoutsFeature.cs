using NServiceBus;
using NServiceBus.

namespace NServiceBus.Transport.Msmq.Timeouts
{
    /// <summary>
    ///
    /// </summary>
    public class NativeTimeoutsFeature : Feature
    {
        public NativeTimeoutsFeature() => EnableByDefault();

        protected override void Setup(FeatureConfigurationContext context) =>
            context.AddSatelliteReceiver(
                name: "MyCustomSatellite",
                transportAddress: "targetQueue",
                runtimeSettings: PushRuntimeSettings.Default,
                recoverabilityPolicy: (config, errorContext) =>
                {
                    return RecoverabilityAction.MoveToError(config.Failed.ErrorQueue);
                },
                onMessage: OnMessage);

        private Task OnMessage(IBuilder builder, MessageContext context)
        {
            // Implement what this satellite needs to do once it receives a message
            string messageId = context.MessageId;
            return Task.CompletedTask;
        }
    }
}