namespace NServiceBus.Transport.Msmq.DelayedDelivery.Sql
{
    class SqlNameHelper
    {
        const string Prefix = "[";
        const string Suffix = "]";

        public static string Quote(string unquotedName)
        {
            if (unquotedName == null)
            {
                return null;
            }
            return Prefix + unquotedName.Replace(Suffix, Suffix + Suffix) + Suffix;
        }

        public static string Unquote(string quotedString)
        {
            if (quotedString == null)
            {
                return null;
            }

            if (!quotedString.StartsWith(Prefix) || !quotedString.EndsWith(Suffix))
            {
                return quotedString;
            }

            return quotedString
                .Substring(Prefix.Length, quotedString.Length - Prefix.Length - Suffix.Length).Replace(Suffix + Suffix, Suffix);
        }
    }
}