namespace NServiceBus.Transport.Msmq.Tests
{
    using Logging;
    using NUnit.Framework;
    using Testing;

    [SetUpFixture]
    public class TestingLoggerSetup
    {
        [OneTimeSetUp]
        public void SetupTestingLogger() => LogManager.Use<TestingLoggerFactory>();
    }
}