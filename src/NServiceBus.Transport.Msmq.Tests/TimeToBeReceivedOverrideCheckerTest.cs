namespace NServiceBus.Transport.Msmq.Tests
{
    using NUnit.Framework;

    [TestFixture]
    public class TimeToBeReceivedOverrideCheckerTest
    {
        [Test]
        public void Should_succeed_on_non_transactional()
        {
            var result = TimeToBeReceivedOverrideChecker.Check(isTransactional: false, outBoxRunning: false, auditTTBROverridden: false);
            Assert.IsTrue(result.Succeeded);
        }

        [Test]
        public void Should_succeed_on_enabled_outbox()
        {
            var result = TimeToBeReceivedOverrideChecker.Check(isTransactional: true, outBoxRunning: true, auditTTBROverridden: false);
            Assert.IsTrue(result.Succeeded);
        }

        [Test]
        public void Should_fail_on_overridden_audit_TimeToBeReceived()
        {
            var result = TimeToBeReceivedOverrideChecker.Check(isTransactional: true, outBoxRunning: false, auditTTBROverridden: true);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual("Setting a custom OverrideTimeToBeReceived for audits is not supported on transactional MSMQ.", result.ErrorMessage);
        }
    }
}