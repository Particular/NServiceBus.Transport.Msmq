namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Transactions;
    using AcceptanceTesting;
    using AcceptanceTesting.Customization;
    using Logging;
    using Microsoft.Data.SqlClient;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_deferring_a_batch_of_messages : NServiceBusAcceptanceTest
    {
        static readonly TimeSpan Delay = TimeSpan.FromSeconds(25);
        const int NrOfDelayedMessages = 1000;

        [Test]
        [TestCase(TransportTransactionMode.None)]
        [TestCase(TransportTransactionMode.ReceiveOnly)]
        [TestCase(TransportTransactionMode.SendsAtomicWithReceive)]
        [TestCase(TransportTransactionMode.TransactionScope)]
        public async Task Should_work_for_transaction_mode(TransportTransactionMode transactionMode)
        {
            Requires.DelayedDelivery();

            var deliverAt = DateTimeOffset.UtcNow + Delay; //To ensure this timeout would never be actually dispatched during this test

            Log.Info($"Dispatch at {deliverAt:O}");

            var context = await Scenario.Define<Context>()
                .WithEndpoint<Endpoint>(b =>
                {
                    b.CustomConfig(cfg =>
                    {
                        cfg.ConfigureTransport<MsmqTransport>().TransportTransactionMode = transactionMode;
                    });
                    b.When(async (session, c) =>
                    {
                        await PurgeTimeoutsTable().ConfigureAwait(false);

                        var s = Stopwatch.StartNew();
                        var sendTasks = new List<Task>(NrOfDelayedMessages);

                        var limiter = new SemaphoreSlim(16);

                        for (int i = 0; i < NrOfDelayedMessages; i++)
                        {
                            var options = new SendOptions();

                            options.DoNotDeliverBefore(deliverAt);
                            options.RouteToThisEndpoint();
                            await limiter.WaitAsync().ConfigureAwait(false);
                            sendTasks.Add(Task.Run(() => session.Send(new MyMessage(), options)).ContinueWith(t => limiter.Release()));
                        }

                        await Task.WhenAll(sendTasks).ConfigureAwait(false);
                        var duration = s.Elapsed;

                        Log.InfoFormat($"Sending {NrOfDelayedMessages} delayed messages took {duration}.");
                        Log.InfoFormat("Storing...");
                        c.StoringTimeouts.Wait();
                        Log.InfoFormat($"Storing took roughly {s.Elapsed} (include sending)");

                        Assert.Less(DateTimeOffset.UtcNow, deliverAt, "Storing not happened within timeout due timestamp");
                        Log.InfoFormat("Dispatching...");
                        c.DispatchingTimeouts.Wait();
                        var dispatchDuration = DateTimeOffset.UtcNow - deliverAt;
                        Log.InfoFormat($"Dispatching took {dispatchDuration}");
                        Log.InfoFormat("Processing...");
                        c.Processed.Wait();
                        Log.InfoFormat("Done!");
                        var affected = await PurgeTimeoutsTable().ConfigureAwait(false);
                        Assert.AreEqual(0, affected, "No rows should remain in database.");
                    }).DoNotFailOnErrorMessages();
                })
                .Run();
        }

#pragma warning disable PS0018 // A task-returning method should have a CancellationToken parameter unless it has a parameter implementing ICancellableContext
        static async Task<int> PurgeTimeoutsTable()
#pragma warning restore PS0018 // A task-returning method should have a CancellationToken parameter unless it has a parameter implementing ICancellableContext
        {
            using (var cn = new SqlConnection(ConfigureEndpointMsmqTransport.GetStorageConnectionString()))
            {
                await cn.OpenAsync();
                using (var cmd = new SqlCommand($"DELETE FROM [nservicebus].[dbo].[{Conventions.EndpointNamingConvention(typeof(Endpoint))}.timeouts]", cn))
                {
                    return await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public class Context : ScenarioContext
        {
            public readonly CountdownEvent StoringTimeouts = new CountdownEvent(NrOfDelayedMessages);
            public readonly CountdownEvent DispatchingTimeouts = new CountdownEvent(NrOfDelayedMessages);
            public readonly CountdownEvent Processed = new CountdownEvent(NrOfDelayedMessages);
        }

        public class Endpoint : EndpointConfigurationBuilder
        {
            public Endpoint()
            {
                EndpointSetup<DefaultServer>((endpointConfiguration, run) =>
                {
                    var context = (Context)run.ScenarioContext;
                    var transport = endpointConfiguration.ConfigureTransport<MsmqTransport>();
                    var storage = new WrapDelayedMessageStore(transport.DelayedDelivery.DelayedMessageStore, context);
                    transport.DelayedDelivery =
                        new DelayedDeliverySettings(storage)
                        {
                            NumberOfRetries = 2,
                            TimeToTriggerStoreCircuitBreaker = TimeSpan.FromSeconds(5)
                        };
                });
            }

            public class MyMessageHandler : IHandleMessages<MyMessage>
            {
                public MyMessageHandler(Context testContext)
                {
                    this.testContext = testContext;
                }

                public Task Handle(MyMessage message, IMessageHandlerContext context)
                {
                    testContext.Processed.Signal();
                    return Task.FromResult(0);
                }

                Context testContext;
            }
        }

        public class MyMessage : IMessage
        {
        }

        class WrapDelayedMessageStore : IDelayedMessageStore
        {
            readonly IDelayedMessageStore delayedMessageStoreImplementation;
            readonly Context context;

            public WrapDelayedMessageStore(IDelayedMessageStore impl, Context context)
            {
                this.context = context;
                delayedMessageStoreImplementation = impl;
            }

            public Task Initialize(string endpointName, TransportTransactionMode transactionMode, CancellationToken cancellationToken = default)
                => delayedMessageStoreImplementation.Initialize(endpointName, transactionMode, cancellationToken);

            public Task<DateTimeOffset?> Next(CancellationToken cancellationToken = default)
                => delayedMessageStoreImplementation.Next(cancellationToken);

            public Task Store(DelayedMessage entity, CancellationToken cancellationToken = default)
            {
                Transaction.Current.TransactionCompleted += (s, e) => context.StoringTimeouts.Signal();
                return delayedMessageStoreImplementation.Store(entity, cancellationToken);
            }

            public Task<bool> IncrementFailureCount(DelayedMessage timeout, CancellationToken cancellationToken = default)
                => delayedMessageStoreImplementation.IncrementFailureCount(timeout, cancellationToken);

            public Task<bool> Remove(DelayedMessage entity, CancellationToken cancellationToken = default)
                => delayedMessageStoreImplementation.Remove(entity, cancellationToken);

            public async Task<DelayedMessage> FetchNextDueTimeout(DateTimeOffset at, CancellationToken cancellationToken = default)
            {
                var entity = await delayedMessageStoreImplementation.FetchNextDueTimeout(at, cancellationToken);

                if (entity != null)
                {
                    context.DispatchingTimeouts.Signal();
                }

                return entity;
            }
        }

        static readonly ILog Log = LogManager.GetLogger<When_deferring_a_batch_of_messages>();
    }
}
