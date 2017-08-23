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
            const string endpointName = "ThisisaloooooooooooooooooooooooooooooooooooooooooooooooooooongQueeeeeeeeeeeeeeeeeeeeeeeeeeee1234";
            var ex = Assert.Throws<InvalidOperationException>(() => CheckEndpointNameComplianceForMsmq.Check(endpointName));
            StringAssert.Contains(endpointName, ex.Message);
        }

        [Test]
        public void Should_not_throw_if_endpoint_name_is_compliant()
        {
            const string endpointName = "ThisisaloooooooooooooooooooooooooooooooooooooooooooooooooooongQueeeeeeeeeeeeeeeeeeeeeeeeeeee12";
            Assert.DoesNotThrow(() => CheckEndpointNameComplianceForMsmq.Check(endpointName));
        }
    }
}
