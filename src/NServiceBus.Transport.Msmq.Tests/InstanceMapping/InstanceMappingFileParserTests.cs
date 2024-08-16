namespace NServiceBus.Transport.Msmq.Tests
{
    using System.Xml.Linq;
    using NUnit.Framework;
    using Routing;

    [TestFixture]
    public class InstanceMappingFileParserTests
    {
        [Test]
        public void It_can_parse_valid_file()
        {
            const string xml = @"
<endpoints>
    <endpoint name=""A"">
        <instance discriminator=""D1"" prop1=""V1"" prop2=""V2""/>
        <instance prop3=""V3"" prop4=""V4""/>
    </endpoint>
    <endpoint name=""B"">
        <instance discriminator=""D2"" prop5=""V5"" prop6=""V6""/>
    </endpoint>
</endpoints>
";
            var doc = XDocument.Parse(xml);
            var result = new InstanceMappingFileParser().Parse(doc);

            Assert.That(result, Is.EqualTo(new[]
            {
                new EndpointInstance("A", "D1").SetProperty("prop1", "V1").SetProperty("prop2","V2"),
                new EndpointInstance("A").SetProperty("prop3", "V3").SetProperty("prop4", "V4"),
                new EndpointInstance("B", "D2").SetProperty("prop5", "V5").SetProperty("prop6", "V6")
            }).AsCollection);
        }

        [Test]
        public void It_allows_empty_endpoints_element()
        {
            const string xml = @"
<endpoints>
</endpoints>
";
            var doc = XDocument.Parse(xml);
            var parser = new InstanceMappingFileParser();

            Assert.DoesNotThrow(() => parser.Parse(doc));
        }
    }
}
