namespace NServiceBus.Transport.Msmq.Tests
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Extensions.Logging.Testing;
    using NUnit.Framework;
    using Routing;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml.Linq;

    [TestFixture]
    public class InstanceMappingFileMonitorTests
    {
        FakeLogger<InstanceMappingFileMonitor> logger;

        [SetUp]
        public void Setup() => logger = new FakeLogger<InstanceMappingFileMonitor>();

        [Test]
        public void Reload_should_throw_when_file_does_not_exist()
        {
            var timer = new FakeTimer();
            var fileAccessException = new Exception("Simulated");
            var loader = new FakeLoader(() => throw fileAccessException);
            var monitor = new InstanceMappingFileMonitor(TimeSpan.Zero, timer, loader, new EndpointInstances(), logger);

            var exception = Assert.Throws<Exception>(() => monitor.ReloadData());

            Assert.That(exception.Message, Does.Contain("An error occurred while reading the endpoint instance mapping (NServiceBus.Transport.Msmq.Tests.InstanceMappingFileMonitorTests+FakeLoader). See the inner exception for more details."));
            Assert.That(exception.InnerException, Is.EqualTo(fileAccessException));
        }

        [Test]
        public async Task It_logs_error_when_file_access_fails_during_runtime()
        {
            var errorCallbackInvoked = false;
            var timer = new FakeTimer(ex =>
            {
                errorCallbackInvoked = true;
            });
            var fail = false;
            var loader = new FakeLoader(() =>
            {
                if (fail)
                {
                    throw new Exception("Simulated");
                }
                return XDocument.Parse(@"<endpoints><endpoint name=""A""><instance/></endpoint></endpoints>");
            });

            var monitor = new InstanceMappingFileMonitor(TimeSpan.Zero, timer, loader, new EndpointInstances(), logger);
            await monitor.Start(null);

            fail = true;
            await timer.Trigger();

            Assert.That(errorCallbackInvoked, Is.True);
        }

        [Test]
        public void Should_log_added_endpoints()
        {
            var loader = new FakeLoader(() => XDocument.Parse(@"<endpoints><endpoint name=""A""><instance discriminator=""1"" /><instance discriminator=""2"" /></endpoint></endpoints>"));
            var monitor = new InstanceMappingFileMonitor(TimeSpan.Zero, new FakeTimer(), loader, new EndpointInstances(), logger);

            monitor.ReloadData();

            Assert.That(logger.LatestRecord.Message, Does.Contain(@"Added endpoint 'A' with 2 instances"));
        }

        [Test]
        public void Should_log_removed_endpoints()
        {
            var fileData = new Queue<string>();
            fileData.Enqueue(@"<endpoints><endpoint name=""A""><instance discriminator=""1"" /><instance discriminator=""2"" /></endpoint></endpoints>");
            fileData.Enqueue("<endpoints></endpoints>");
            var loader = new FakeLoader(() => XDocument.Parse(fileData.Dequeue()));
            var monitor = new InstanceMappingFileMonitor(TimeSpan.Zero, new FakeTimer(), loader, new EndpointInstances(), logger);

            monitor.ReloadData();
            logger.Collector.Clear();
            monitor.ReloadData();

            Assert.That(logger.LatestRecord.Message, Does.Contain(@"Removed all instances of endpoint 'A'"));
        }

        [Test]
        public void Should_log_changed_instances()
        {
            var fileData = new Queue<string>();
            fileData.Enqueue(@"<endpoints><endpoint name=""A""><instance discriminator=""1"" /><instance discriminator=""2"" /></endpoint></endpoints>");
            fileData.Enqueue(@"<endpoints><endpoint name=""A""><instance discriminator=""1"" /><instance discriminator=""3"" /><instance discriminator=""4"" /></endpoint></endpoints>");
            var loader = new FakeLoader(() => XDocument.Parse(fileData.Dequeue()));
            var monitor = new InstanceMappingFileMonitor(TimeSpan.Zero, new FakeTimer(), loader, new EndpointInstances(), logger);

            monitor.ReloadData();
            logger.Collector.Clear();
            monitor.ReloadData();

            Assert.That(logger.LatestRecord.Message, Does.Contain(@"Updated endpoint 'A': +2 instances, -1 instance"));
        }

        class FakeLoader(Func<XDocument> docCallback) : IInstanceMappingLoader
        {
            public XDocument Load() => docCallback();
        }

        class FakeTimer(Action<Exception> errorSpyCallback = null) : IAsyncTimer
        {
            Func<CancellationToken, Task> theCallback;
            Action<Exception> theErrorCallback;

            public async Task Trigger(CancellationToken cancellationToken = default)
            {
                try
                {
                    await theCallback(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!ex.IsCausedBy(cancellationToken))
                {
                    theErrorCallback(ex);
                    errorSpyCallback?.Invoke(ex);
                }
            }

            public void Start(Func<CancellationToken, Task> callback, TimeSpan interval, Action<Exception> errorCallback)
            {
                theCallback = callback;
                theErrorCallback = errorCallback;
            }

            public Task Stop(CancellationToken cancellationToken = default) => Task.CompletedTask;
        }
    }
}