namespace NServiceBus.Transport.Msmq.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using System.Xml.Linq;
    using Logging;
    using Routing;
    using NUnit.Framework;
    using Testing;

    [TestFixture]
    public class InstanceMappingFileMonitorTests
    {
        StringBuilder logOutput;

        [OneTimeSetUp]
        public void TestFixtureSetup()
        {
            var loggerFactory = LogManager.Use<TestingLoggerFactory>();
            loggerFactory.Level(LogLevel.Info);
            logOutput = new StringBuilder();
            var stringWriter = new StringWriter(logOutput);
            loggerFactory.WriteTo(stringWriter);
        }

        [SetUp]
        public void Setup()
        {
            logOutput.Clear();
        }

        [Test]
        public void Reload_should_throw_when_file_does_not_exist()
        {
            var timer = new FakeTimer();
            var fileAccessException = new Exception("Simulated");
            var loader = new FakeLoader(() => throw fileAccessException);
            var monitor = new InstanceMappingFileMonitor(TimeSpan.Zero, timer, loader, new EndpointInstances());

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

            var monitor = new InstanceMappingFileMonitor(TimeSpan.Zero, timer, loader, new EndpointInstances());
            await monitor.Start(null);

            fail = true;
            await timer.Trigger();

            Assert.IsTrue(errorCallbackInvoked);
        }

        [Test]
        public void Should_log_added_endpoints()
        {
            var loader = new FakeLoader(() => XDocument.Parse(@"<endpoints><endpoint name=""A""><instance discriminator=""1"" /><instance discriminator=""2"" /></endpoint></endpoints>"));
            var monitor = new InstanceMappingFileMonitor(TimeSpan.Zero, new FakeTimer(), loader, new EndpointInstances());

            monitor.ReloadData();

            Assert.That(logOutput.ToString(), Does.Contain(@"Added endpoint 'A' with 2 instances"));
        }

        [Test]
        public void Should_log_removed_endpoints()
        {
            var fileData = new Queue<string>();
            fileData.Enqueue(@"<endpoints><endpoint name=""A""><instance discriminator=""1"" /><instance discriminator=""2"" /></endpoint></endpoints>");
            fileData.Enqueue("<endpoints></endpoints>");
            var loader = new FakeLoader(() => XDocument.Parse(fileData.Dequeue()));
            var monitor = new InstanceMappingFileMonitor(TimeSpan.Zero, new FakeTimer(), loader, new EndpointInstances());

            monitor.ReloadData();
            logOutput.Clear();
            monitor.ReloadData();

            Assert.That(logOutput.ToString(), Does.Contain(@"Removed all instances of endpoint 'A'"));
        }

        [Test]
        public void Should_log_changed_instances()
        {
            var fileData = new Queue<string>();
            fileData.Enqueue(@"<endpoints><endpoint name=""A""><instance discriminator=""1"" /><instance discriminator=""2"" /></endpoint></endpoints>");
            fileData.Enqueue(@"<endpoints><endpoint name=""A""><instance discriminator=""1"" /><instance discriminator=""3"" /><instance discriminator=""4"" /></endpoint></endpoints>");
            var loader = new FakeLoader(() => XDocument.Parse(fileData.Dequeue()));
            var monitor = new InstanceMappingFileMonitor(TimeSpan.Zero, new FakeTimer(), loader, new EndpointInstances());

            monitor.ReloadData();
            logOutput.Clear();
            monitor.ReloadData();

            Assert.That(logOutput.ToString(), Does.Contain(@"Updated endpoint 'A': +2 instances, -1 instance"));
        }

        class FakeLoader : IInstanceMappingLoader
        {
            readonly Func<XDocument> docCallback;

            public FakeLoader(Func<XDocument> docCallback)
            {
                this.docCallback = docCallback;
            }

            public XDocument Load() => docCallback();
        }

        class FakeTimer : IAsyncTimer
        {
            Func<Task> theCallback;
            Action<Exception> theErrorCallback;
            Action<Exception> errorSpyCallback;

            public FakeTimer(Action<Exception> errorSpyCallback = null)
            {
                this.errorSpyCallback = errorSpyCallback;
            }

            public async Task Trigger()
            {
                try
                {
                    await theCallback().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    theErrorCallback(ex);
                    errorSpyCallback?.Invoke(ex);
                }
            }

            public void Start(Func<Task> callback, TimeSpan interval, Action<Exception> errorCallback)
            {
                theCallback = callback;
                theErrorCallback = errorCallback;
            }

            public Task Stop() => TaskEx.CompletedTask;
        }
    }
}