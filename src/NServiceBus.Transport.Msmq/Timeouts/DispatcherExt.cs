using System.Linq;
using System.Threading.Tasks;
using NServiceBus.Routing;
using NServiceBus.Transport;
using NServiceBus.Transport.Msmq;

static class DispatcherExt
{
    public static Task Dispatch(this IMessageDispatcher instance, string id, byte[] extension, byte[] body, string destination, TransportTransaction transportTransaction)
    {
        var headers = MsmqUtilities.DeserializeMessageHeaders(extension);

        headers = headers
            .Where(kv => !kv.Key.StartsWith(MsmqUtilities.PropertyHeaderPrefix))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        
        var request = new OutgoingMessage(
            messageId: id,
            headers: headers,
            body: body);

        var operation = new TransportOperation(
            request,
            new UnicastAddressTag(destination));

        return instance.Dispatch(
            outgoingMessages: new TransportOperations(operation),
            transaction: transportTransaction
        );
    }}