namespace Messaging.Msmq
{
    static class ValidationUtility
    {
        public static bool ValidateAccessControlEntryType(AccessControlEntryType value)
        {
            return value is >= AccessControlEntryType.Allow and <= AccessControlEntryType.Revoke;
        }

        public static bool ValidateCryptographicProviderType(CryptographicProviderType value)
        {
            return value is >= CryptographicProviderType.None and <= CryptographicProviderType.SttIss;
        }

        public static bool ValidateEncryptionAlgorithm(EncryptionAlgorithm value)
        {
            //
            // note that EncryptionAlgorithm has disjoined values
            //
            return value is EncryptionAlgorithm.None or
                   EncryptionAlgorithm.Rc2 or
                   EncryptionAlgorithm.Rc4;
        }

        public static bool ValidateEncryptionRequired(EncryptionRequired value)
        {
            return value is >= EncryptionRequired.None and <= EncryptionRequired.Body;
        }

        public static bool ValidateHashAlgorithm(HashAlgorithm value)
        {
            //
            // note that HashAlgorithm has disjoined values
            //
            return value is HashAlgorithm.None or
                   HashAlgorithm.Md2 or
                   HashAlgorithm.Md4 or
                   HashAlgorithm.Md5 or
                   HashAlgorithm.Sha or
                   HashAlgorithm.Sha256 or
                   HashAlgorithm.Sha384 or
                   HashAlgorithm.Sha512 or
                   HashAlgorithm.Mac;
        }

        public static bool ValidateMessageLookupAction(MessageLookupAction value)
        {
            //
            // note that MessageLookupAction has disjoined values
            //
            return value is MessageLookupAction.Current or
                   MessageLookupAction.Next or
                   MessageLookupAction.Previous or
                   MessageLookupAction.First or
                   MessageLookupAction.Last;
        }

        public static bool ValidateMessagePriority(MessagePriority value)
        {
            return value is >= MessagePriority.Lowest and <= MessagePriority.Highest;

        }

        public static bool ValidateMessageQueueTransactionType(MessageQueueTransactionType value)
        {
            //
            // note that MessageQueueTransactionType has disjoined values
            //
            return value is MessageQueueTransactionType.None or
                   MessageQueueTransactionType.Automatic or
                   MessageQueueTransactionType.Single;
        }

        public static bool ValidateQueueAccessMode(QueueAccessMode value)
        {
            //
            // note that QueueAccessMode has disjoined values
            //
            return value is QueueAccessMode.Send or
                   QueueAccessMode.Peek or
                   QueueAccessMode.Receive or
                   QueueAccessMode.PeekAndAdmin or
                   QueueAccessMode.ReceiveAndAdmin or
                   QueueAccessMode.SendAndReceive;
        }

        public static bool ValidateTrusteeType(TrusteeType trustee)
        {
            return trustee is >= TrusteeType.Unknown and <= TrusteeType.Computer;
        }

    } //class ValidationUtility
}

