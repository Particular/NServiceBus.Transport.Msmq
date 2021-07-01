namespace NServiceBus.Transport.Msmq.AcceptanceTests
{
    using System;
    using System.Collections.Generic;
    using System.Data.SqlClient;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Transactions;
    using AcceptanceTesting;
    using Logging;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_deferring_a_batch_of_messages : NServiceBusAcceptanceTest
    {
        static readonly TimeSpan Delay = TimeSpan.FromSeconds(25);
        const int NrOfDelayedMessages = 10000;

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

            //Assert.False(context.Processed);
            //Assert.True(context.CriticalActionCalled);
        }

        static async Task<int> PurgeTimeoutsTable()
        {
            using (var cn = new SqlConnection(ConfigureEndpointMsmqTransport.GetStorageConnectionString()))
            {
                await cn.OpenAsync();
                using (var cmd = new SqlCommand("DELETE FROM [nservicebus].[dbo].[SendingDelayedMessages.EndpointTimeouts]", cn))
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
                    //transport.TransportTransactionMode = TransportTransactionMode.ReceiveOnly;

                    var storage = new WrapTimeoutStorage(transport.DelayedDelivery.TimeoutStorage, context);
                    transport.DelayedDelivery = new DelayedDeliverySettings(
                        storage,
                        2,
                        TimeSpan.FromSeconds(5)
                        );
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

        class WrapTimeoutStorage : ITimeoutStorage
        {
            readonly ITimeoutStorage timeoutStorageImplementation;
            readonly Context context;

            public WrapTimeoutStorage(ITimeoutStorage impl, Context context)
            {
                this.context = context;
                timeoutStorageImplementation = impl;
            }
            public Task Initialize(string endpointName, CancellationToken cancellationToken) => timeoutStorageImplementation.Initialize(endpointName, cancellationToken);

            public Task<DateTimeOffset?> Next() => timeoutStorageImplementation.Next();

            public Task Store(TimeoutItem entity)
            {
                Transaction.Current.TransactionCompleted += (s, e) => context.StoringTimeouts.Signal();
                return timeoutStorageImplementation.Store(entity);
            }
            public Task<bool> BumpFailureCount(TimeoutItem timeout) => timeoutStorageImplementation.BumpFailureCount(timeout);

            public Task<bool> Remove(TimeoutItem entity) => timeoutStorageImplementation.Remove(entity);
            public async Task<TimeoutItem> FetchNextDueTimeout(DateTimeOffset at)
            {
                var entity = await timeoutStorageImplementation.FetchNextDueTimeout(at);

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
