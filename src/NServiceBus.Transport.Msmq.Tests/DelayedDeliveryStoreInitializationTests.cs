namespace NServiceBus.Transport.Msmq.Tests
{
    using System;
    using System.Messaging;
    using System.Threading;
    using System.Threading.Tasks;
    using NServiceBus;
    using NUnit.Framework;

    [TestFixture]
    class DelayedDeliveryStoreInitializationTests
    {
        const string EndpointName = "Test";
        TransportInfrastructure transportInfrastructure;

        [TestCase(true, true, ExpectedResult = true)]
        [TestCase(true, false, ExpectedResult = false)]
        [TestCase(false, true, ExpectedResult = false)]
        [TestCase(false, false, ExpectedResult = false)]
        public async Task<bool> Should_only_call_initialize_on_legacy_interface_when_configured_to_create_queues(bool setupInfrastrutcure, bool createQueues)
        {
            var messageStore = new FakeDelayedMessageStore();

            await InitializeTransport(messageStore, setupInfrastrutcure, createQueues);

            return messageStore.WasInitialized;
        }


        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public async Task Should_always_call_initialize_on_new_interface(bool setupInfrastrutcure, bool createQueues)
        {
            var messageStore = new FakeDelayedMessageStoreWithInfrastructure();

            await InitializeTransport(messageStore, setupInfrastrutcure, createQueues);

            Assert.That(messageStore.WasInitialized, Is.True);
        }

        [TestCase(true, true, ExpectedResult = true)]
        [TestCase(true, false, ExpectedResult = false)]
        [TestCase(false, true, ExpectedResult = false)]
        [TestCase(false, false, ExpectedResult = false)]
        public async Task<bool> Should_only_call_setup_infrastructure_on_new_interface_when_configured_to_create_queues(bool setupInfrastructure, bool createQueues)
        {
            var messageStore = new FakeDelayedMessageStoreWithInfrastructure();

            await InitializeTransport(messageStore, setupInfrastructure, createQueues);

            return messageStore.SetupInfrastructureCalled;
        }

        [SetUp]
        public void Setup()
        {
            MsmqHelpers.CreateQueue($@".\private$\{EndpointName}");
            MsmqHelpers.CreateQueue($@".\private$\{EndpointName}.Timeouts");
            MsmqHelpers.CreateQueue($@".\private$\error");
        }

        [TearDown]
        public async Task Teardown()
        {
            await transportInfrastructure.Shutdown();
            MsmqHelpers.DeleteQueue($@".\private$\{EndpointName}");
            MsmqHelpers.DeleteQueue($@".\private$\{EndpointName}.Timeouts");
            MsmqHelpers.DeleteQueue($@".\private$\error");
            MessageQueue.ClearConnectionCache();
        }

        async Task InitializeTransport(IDelayedMessageStore messageStore, bool setupInfrastructure, bool createQueues, CancellationToken cancellationToken = default)
        {
            var transport = new MsmqTransport
            {
                DelayedDelivery = new DelayedDeliverySettings(messageStore),
                CreateQueues = createQueues
            };

            var hostSettings = new HostSettings(EndpointName, EndpointName, new StartupDiagnosticEntries(), CriticalErrorAction, setupInfrastructure);

            var recievers = new ReceiveSettings[]
            {
                new ReceiveSettings("Main", new QueueAddress(EndpointName), false, false, "error")
            };

            var sendingAddresses = Array.Empty<string>();

            transportInfrastructure = await transport.Initialize(hostSettings, recievers, sendingAddresses, cancellationToken);

            void CriticalErrorAction(string s, Exception e, CancellationToken c)
            {
            }
        }

        class FakeDelayedMessageStore : IDelayedMessageStore
        {
            public bool WasInitialized { get; set; }

            public Task<DelayedMessage> FetchNextDueTimeout(DateTimeOffset at, CancellationToken cancellationToken = default) => Task.FromResult(default(DelayedMessage));
            public Task<bool> IncrementFailureCount(DelayedMessage entity, CancellationToken cancellationToken = default) => Task.FromResult(default(bool));
            public Task Initialize(string endpointName, TransportTransactionMode transactionMode, CancellationToken cancellationToken = default)
            {
                WasInitialized = true;
                return Task.CompletedTask;
            }
            public Task<DateTimeOffset?> Next(CancellationToken cancellationToken = default) => Task.FromResult(default(DateTimeOffset?));
            public Task<bool> Remove(DelayedMessage entity, CancellationToken cancellationToken = default) => Task.FromResult(default(bool));
            public Task Store(DelayedMessage entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        class FakeDelayedMessageStoreWithInfrastructure : FakeDelayedMessageStore, IDelayedMessageStoreWithInfrastructure
        {
            public bool SetupInfrastructureCalled { get; set; }

            public Task SetupInfrastructure(CancellationToken cancellationToken = default)
            {
                SetupInfrastructureCalled = true;
                return Task.CompletedTask;
            }
        }
    }
}
