namespace NServiceBus.Persistence.Msmq
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Extensibility;
    using Logging;
    using Transport.Msmq;
    using Unicast.Subscriptions.MessageDrivenSubscriptions;
    using MessageType = Unicast.Subscriptions.MessageType;

    class MsmqSubscriptionStorage : IInitializableSubscriptionStorage, IDisposable
    {
        public MsmqSubscriptionStorage(IMsmqSubscriptionStorageQueue storageQueue)
        {
            this.storageQueue = storageQueue;
        }

        public void Dispose()
        {
            // Filled in by Janitor.fody
        }

        public void Init()
        {
            var messages = storageQueue.GetAllMessages()
                .OrderByDescending(m => m.ArrivedTime)
                .ThenBy(x => x.Id) // ensure same order of messages with same timestamp across all endpoints
                .ToArray();
            var newLookup = new Dictionary<Subscriber, Dictionary<MessageType, string>>(SubscriberComparer);

            foreach (var m in messages)
            {
                var messageTypeString = m.Body as string;
                var messageType = new MessageType(messageTypeString); //this will parse both 2.6 and 3.0 type strings
                var subscriber = Deserialize(m.Label);

                if (!newLookup.TryGetValue(subscriber, out var endpointSubscriptions))
                {
                    newLookup[subscriber] = endpointSubscriptions = new Dictionary<MessageType, string>();
                }

                if (endpointSubscriptions.ContainsKey(messageType))
                {
                    // this message is stale and can be removed
                    storageQueue.TryReceiveById(m.Id);
                }
                else
                {
                    endpointSubscriptions[messageType] = m.Id;
                }
            }

            lookup = newLookup;
        }

        public Task<IEnumerable<Subscriber>> GetSubscriberAddressesForMessage(IEnumerable<MessageType> messageTypes, ContextBag context)
        {
            var messagelist = messageTypes.ToArray();
            var result = new HashSet<Subscriber>();

            foreach (var subscribers in lookup)
            {
                foreach (var messageType in messagelist)
                {
                    if (subscribers.Value.TryGetValue(messageType, out _))
                    {
                        result.Add(subscribers.Key);
                    }
                }
            }

            return Task.FromResult<IEnumerable<Subscriber>>(result);
        }

        public Task Subscribe(Subscriber subscriber, MessageType messageType, ContextBag context)
        {
            var body = $"{messageType.TypeName}, Version={messageType.Version}";
            var label = Serialize(subscriber);
            storageQueue.Send(body, label);

            log.DebugFormat($"Subscriber {subscriber.TransportAddress} added for message {messageType}.");

            Init(); // Reload, which will dedupe storage entries FIFO.

            return TaskEx.CompletedTask;
        }

        public Task Unsubscribe(Subscriber subscriber, MessageType messageType, ContextBag context)
        {
            var messageId = GetFromLookup(subscriber, messageType);

            if (messageId != null)
            {
                storageQueue.TryReceiveById(messageId);
            }

            log.Debug($"Subscriber {subscriber.TransportAddress} removed for message {messageType}.");

            Init(); // Reload, which will dedupe storage entries FIFO.

            return TaskEx.CompletedTask;
        }

        static string Serialize(Subscriber subscriber)
        {
            return $"{subscriber.TransportAddress}|{subscriber.Endpoint}";
        }

        static Subscriber Deserialize(string serializedForm)
        {
            var parts = serializedForm.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts.Length > 2)
            {
                log.Error($"Invalid format of subscription entry: {serializedForm}.");
                return null;
            }
            var endpointName = parts.Length > 1
                ? parts[1]
                : null;

            return new Subscriber(parts[0], endpointName);
        }

        string GetFromLookup(Subscriber subscriber, MessageType typeName)
        {
            if (lookup.TryGetValue(subscriber, out var subscriptions))
            {
                if (subscriptions.TryGetValue(typeName, out var messageId))
                {
                    return messageId;
                }
            }

            return null;
        }

        Dictionary<Subscriber, Dictionary<MessageType, string>> lookup;
        IMsmqSubscriptionStorageQueue storageQueue;

        static ILog log = LogManager.GetLogger(typeof(ISubscriptionStorage));
        static TransportAddressEqualityComparer SubscriberComparer = new TransportAddressEqualityComparer();

        static readonly char[] EntrySeparator =
        {
            '|'
        };

        sealed class TransportAddressEqualityComparer : IEqualityComparer<Subscriber>
        {
            public bool Equals(Subscriber x, Subscriber y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.GetType() != y.GetType()) return false;
                return string.Equals(x.TransportAddress, y.TransportAddress, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(Subscriber obj)
            {
                return (obj.TransportAddress != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TransportAddress) : 0);
            }
        }
    }
}