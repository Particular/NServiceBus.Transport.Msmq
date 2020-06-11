namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.IO;
    using System.Xml;
    using System.Xml.Linq;

    class InstanceMappingFileAccess : IInstanceMappingFileAccess
    {
        public XDocument Load(string path)
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
            {
                using (var reader = XmlReader.Create(uri.ToString()))
                {
                    return XDocument.Load(reader);
                }
            }
            else
            {
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = XmlReader.Create(file))
                {
                    return XDocument.Load(reader);
                }
            }
        }
    }
}