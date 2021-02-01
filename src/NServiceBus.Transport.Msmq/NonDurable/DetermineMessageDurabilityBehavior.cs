namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading.Tasks;
    using Pipeline;

    class DetermineMessageDurabilityBehavior : IBehavior<IOutgoingLogicalMessageContext, IOutgoingLogicalMessageContext>
    {
        const string NonDurableHeaderName = "NServiceBus.NonDurableMessage";


        public DetermineMessageDurabilityBehavior(Func<Type, bool> convention)
        {
            this.convention = convention;
            durabilityCache = new ConcurrentDictionary<Type, bool>();
        }

        public Task Invoke(IOutgoingLogicalMessageContext context, Func<IOutgoingLogicalMessageContext, Task> next)
        {
            if (durabilityCache.GetOrAdd(context.Message.MessageType, t => convention(t)))
            {
                context.Extensions.Get<DispatchProperties>()
                    .Add(MsmqMessageDispatcher.NonDurableDispatchPropertyKey, bool.TrueString);
                context.Headers[NonDurableHeaderName] = bool.TrueString;
            }

            return next(context);
        }

        readonly Func<Type, bool> convention;
        readonly ConcurrentDictionary<Type, bool> durabilityCache;
    }
}