namespace NServiceBus.Transport.Msmq.Tests
{
    using System.Xml.Linq;
    using System.Xml.Schema;
    using NServiceBus.Transport.Msmq;
    using NUnit.Framework;

    [TestFixture]
    abstract class InstanceMappingValidatorSchemaTests
    {
        protected abstract IInstanceMappingValidator CreateValidator();

        [Test]
        public void It_can_validate_valid_file()
        {
            const string xml = @"
<endpoints>
    <endpoint name=""A"">
        <instance discriminator=""D1"" machine=""COMP-A"" queue=""Q1""/>
        <instance machine=""COMP-B"" queue=""Q2""/>
    </endpoint>
    <endpoint name=""B"">
        <instance discriminator=""D2"" machine=""COMP-C""/>
    </endpoint>
</endpoints>
";
            var doc = XDocument.Parse(xml);
            var validator = CreateValidator();
            Assert.DoesNotThrow(() => validator.Validate(doc));
        }

        [Test]
        public void It_allows_empty_endpoints_element()
        {
            const string xml = @"
<endpoints>
</endpoints>
";
            var doc = XDocument.Parse(xml);
            var validator = CreateValidator();

            Assert.DoesNotThrow(() => validator.Validate(doc));
        }


        [Test]
        public void It_requires_endpoint_name()
        {
            const string xml = @"
<endpoints>
    <endpoint/>
</endpoints>
";
            var doc = XDocument.Parse(xml);
            var validator = CreateValidator();

            var exception = Assert.Throws<XmlSchemaValidationException>(() => validator.Validate(doc));
            Assert.That(exception.Message, Does.Contain("The required attribute 'name' is missing."));
        }

        [Test]
        public void It_requires_endpoint_to_have_an_instance()
        {
            const string xml = @"
<endpoints>
    <endpoint name=""A""/>
</endpoints>
";
            var doc = XDocument.Parse(xml);
            var validator = CreateValidator();

            var exception = Assert.Throws<XmlSchemaValidationException>(() => validator.Validate(doc));
            Assert.That(exception.Message, Does.Contain("The element 'endpoint' has incomplete content. List of possible elements expected: 'instance'."));
        }
    }
}
