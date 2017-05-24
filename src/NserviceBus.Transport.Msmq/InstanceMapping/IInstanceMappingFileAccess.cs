namespace NServiceBus.Transport.Msmq
{
    using System.Xml.Linq;

    interface IInstanceMappingFileAccess
    {
        XDocument Load(string path);
    }
}