// ReSharper disable UnusedTypeParameter
// ReSharper disable UnusedParameter.Local
// ReSharper disable UnusedParameter.Global

#pragma warning disable 1591

namespace NServiceBus.Persistence.Legacy
{
    using System;

    [ObsoleteEx(
        Message = "The classes under the NServiceBus.Persistence.Legacy namespace have been moved. Use NServiceBus.MsmqPersistence instead.",
        RemoveInVersion = "2",
        TreatAsErrorFromVersion = "1")]
    public class MsmqPersistence : PersistenceDefinition { }

    [ObsoleteEx(
        Message = "The classes under the NServiceBus.Persistence.Legacy namespace have been moved. Please use NServiceBus.MsmqSubscriptionStorageConfigurationExtensions instead.",
        RemoveInVersion = "2",
        TreatAsErrorFromVersion = "1")]
    public static class MsmqSubscriptionStorageConfigurationExtensions
    {
        [ObsoleteEx(
            Message = "The classes under the NServiceBus.Persistence.Legacy namespace have been moved. Please use NServiceBus.MsmqPersistence.SubscriptionQueue() instead.",
            RemoveInVersion = "2",
            TreatAsErrorFromVersion = "1")]
        public static void SubscriptionQueue(this PersistenceExtensions<MsmqPersistence> persistenceExtensions, string queue)
        {
            throw new NotImplementedException();
        }
    }
}

#pragma warning restore 1591