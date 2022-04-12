namespace NServiceBus.Transport.Msmq
{
    using Extensibility;

    static class ContextExtensions
    {
        public static ReadOnlyContextBag GetOperationProperties(this ContextBag contextBag)
        {
            if (contextBag.TryGet("NServiceBus.OperationProperties", out ContextBag operationProperties))
            {
                return operationProperties;
            }

            return contextBag; // fallback behavior, e.g. when invoking the outgoing pipeline without using MessageOperation API.
        }
    }
}