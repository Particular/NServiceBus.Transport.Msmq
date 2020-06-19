namespace NServiceBus.Transport.Msmq.Tests
{
    using NUnit.Framework;
    using System.Xml.Linq;

    class InstanceMappingValidatorSchemaV1Tests : InstanceMappingValidatorSchemaTests
    {
        protected override IInstanceMappingValidator CreateValidator() => EmbeddedSchemaInstanceMappingValidator.CreateValidatorV1();

        [Test]
        public void It_allows_arbitrary_instance_attributes()
        {
            const string xml = @"<endpoints>
    <endpoint name=""A"">
        <instance arbitrary=""value"" />
    </endpoint>
</endpoints>";

            var doc = XDocument.Parse(xml);
            var validator = CreateValidator();
            Assert.DoesNotThrow(() => validator.Validate(doc));
        }
    }
}
