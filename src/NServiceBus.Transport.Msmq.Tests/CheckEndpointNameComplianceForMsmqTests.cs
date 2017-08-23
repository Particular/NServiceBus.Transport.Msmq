namespace NServiceBus.Transport.Msmq.Tests
{
    using System;
    using NUnit.Framework;

    [TestFixture]
    public class CheckEndpointNameComplianceForMsmqTests
    {
        [Test]
        public void Should_throw_if_endpoint_name_is_too_long()
        {
            const string endpointNameGreaterThanOneHundredFiftyChars = "ThisisalooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooongQueeeeeeeeeeeeeeeeeeeeeeeeeeee";
            var ex = Assert.Throws<InvalidOperationException>(() => CheckEndpointNameComplianceForMsmq.Check(endpointNameGreaterThanOneHundredFiftyChars));
            StringAssert.Contains(endpointNameGreaterThanOneHundredFiftyChars, ex.Message);
        }

        [Test]
        public void Should_not_throw_if_endpoint_name_is_compliant()
        {
            const string endpointName = "short-decent-endpoint-name";
            Assert.DoesNotThrow(() => CheckEndpointNameComplianceForMsmq.Check(endpointName));
        }
    }
}
