namespace NServiceBus.Transport.Msmq.Tests
{
    using System;
    using NUnit.Framework;

    [TestFixture]
    public class TimeToBeReceivedOverrideCheckerTest
    {
        [Test]
        public void Should_succeed_on_non_transactional()
        {
            Assert.DoesNotThrow(() =>
                TimeToBeReceivedOverrideChecker.Check(isTransactional: false, outBoxRunning: false, auditTTBROverridden: false));
        }

        [Test]
        public void Should_succeed_on_enabled_outbox()
        {
            Assert.DoesNotThrow(() =>
                TimeToBeReceivedOverrideChecker.Check(isTransactional: true, outBoxRunning: true, auditTTBROverridden: false));
        }

        [Test]
        public void Should_fail_on_overridden_audit_TimeToBeReceived()
        {
            var exception = Assert.Throws<Exception>(() =>
                TimeToBeReceivedOverrideChecker.Check(isTransactional: true, outBoxRunning: false,
                    auditTTBROverridden: true));

            Assert.That(exception.Message, Is.EqualTo("Setting a custom OverrideTimeToBeReceived for audits is not supported on transactional MSMQ."));
        }
    }
}