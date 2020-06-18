namespace NServiceBus.Transport.Msmq.Tests
{
    using NServiceBus.Transport.Msmq;
    using NUnit.Framework;
    using System.Xml.Linq;
    using System.Xml.Schema;

    class InstanceMappingValidatorSchemaV2Tests : InstanceMappingValidatorSchemaTests
    {
        protected override IInstanceMappingValidator CreateValidator() => EmbeddedSchemaInstanceMappingValidator.CreateValidatorV2();

        [Test]
        public void It_does_not_allow_arbitrary_instance_attributes()
        {
            const string xml = @"<endpoints>
    <endpoint name=""A"">
        <instance arbitrary=""value"" />
    </endpoint>
</endpoints>";

            var doc = XDocument.Parse(xml);
            var validator = CreateValidator();
            var exception = Assert.Throws<XmlSchemaValidationException>(() => validator.Validate(doc));

            Assert.That(exception.Message, Does.Contain("The 'arbitrary' attribute is not declared."));
        }
    }
}
