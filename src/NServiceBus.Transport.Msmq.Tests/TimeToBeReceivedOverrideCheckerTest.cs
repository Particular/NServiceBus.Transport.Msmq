namespace NServiceBus.Transport.Msmq.Tests
{
    using NUnit.Framework;

    [TestFixture]
    public class TimeToBeReceivedOverrideCheckerTest
    {
        [Test]
        public void Should_succeed_on_non_transactional()
        {
            var result = new NativeTimeToBeReceivedStrategy(isTransactional: false, outBoxRunning: false, auditTTBROverridden: false)
                    .PerformStartupCheck();
                
            Assert.IsTrue(result.Succeeded);
        }

        [Test]
        public void Should_succeed_on_enabled_outbox()
        {
            var result = new NativeTimeToBeReceivedStrategy(isTransactional: true, outBoxRunning: true, auditTTBROverridden: false)
                    .PerformStartupCheck();
            Assert.IsTrue(result.Succeeded);
        }

        [Test]
        public void Should_fail_on_overridden_audit_TimeToBeReceived()
        {
            var result = new NativeTimeToBeReceivedStrategy(isTransactional: true, outBoxRunning: false, auditTTBROverridden: true)
                    .PerformStartupCheck();
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual("Setting a custom OverrideTimeToBeReceived for audits is not supported on transactional MSMQ.", result.ErrorMessage);
        }
    }
}