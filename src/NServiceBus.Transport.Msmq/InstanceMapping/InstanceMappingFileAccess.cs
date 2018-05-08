namespace NServiceBus.Transport.Msmq
{
    using System.IO;
    using System.Xml;
    using System.Xml.Linq;

    class InstanceMappingFileAccess : IInstanceMappingFileAccess
    {
        public XDocument Load(string path)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = XmlReader.Create(file))
            {
                return XDocument.Load(reader);
            }
        }
    }
}