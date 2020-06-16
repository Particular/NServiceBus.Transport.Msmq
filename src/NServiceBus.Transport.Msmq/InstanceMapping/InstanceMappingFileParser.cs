namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml;
    using System.Xml.Linq;
    using System.Xml.Schema;
    using NServiceBus.Logging;
    using Routing;

    class InstanceMappingFileParser
    {
        public InstanceMappingFileParser()
        {
            using (var stream = GetType().Assembly.GetManifestResourceStream("NServiceBus.Transport.Msmq.InstanceMapping.endpoints.xsd"))
            using (var xmlReader = XmlReader.Create(stream??throw new InvalidOperationException("Could not load resource.")))
            {
                schema = new XmlSchemaSet();
                schema.Add("", xmlReader);
            }

            using(var stream = GetType().Assembly.GetManifestResourceStream("NServiceBus.Transport.Msmq.InstanceMapping.endpointsV2.xsd"))
            using(var xmlReader = XmlReader.Create(stream))
            {
                schemaV2 = new XmlSchemaSet();
                schemaV2.Add("", xmlReader);
            }
        }

        public List<EndpointInstance> Parse(XDocument document)
        {

            try
            {
                document.Validate(schemaV2, null, false);
            }
            catch(XmlSchemaValidationException ex)
            {
                Logger.Warn($"Validation error parsing instance mapping. Falling back on relaxed parsing method. Instance mapping may contain unsupported attributes.", ex);

                document.Validate(schema, null, true);
            }

            var root = document.Root;
            var endpointElements = root.Descendants("endpoint");

            var instances = new List<EndpointInstance>();

            foreach (var e in endpointElements)
            {
                var endpointName = e.Attribute("name").Value;

                foreach (var i in e.Descendants("instance"))
                {
                    var discriminatorAttribute = i.Attribute("discriminator");
                    var discriminator = discriminatorAttribute?.Value;

                    var properties = i.Attributes().Where(a => a.Name != "discriminator");
                    var propertyDictionary = properties.ToDictionary(a => a.Name.LocalName, a => a.Value);

                    instances.Add(new EndpointInstance(endpointName, discriminator, propertyDictionary));
                }
            }

            return instances;
        }

        XmlSchemaSet schema;
        XmlSchemaSet schemaV2;

        static ILog Logger = LogManager.GetLogger<InstanceMappingFileParser>();
    }
}