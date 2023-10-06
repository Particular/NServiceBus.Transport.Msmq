namespace Messaging.Msmq
{
    using System.Collections.Frozen;
    using System.Collections.Generic;

    static class Res
    {
        static readonly FrozenDictionary<string, string> resources;

        static Res()
        {
            Dictionary<string, string> data = new()
            {
                { MSMQNotInstalled, "Message Queuing has not been installed on this computer." },
                { PlatformNotSupported, "Requested operation is not supported on this platform." },
                { MSMQInfoNotSupported, "Browsing private queues is not supported by the Microsoft Message Queuing(MSMQ) runtime installed on this computer." },
                { NotAcknowledgement, "Cannot retrieve property because the message is not an acknowledgment message." },
                { NoCurrentMessage, "Cursor is not currently pointing to a Message instance. It is located either before the first or after the last message in the enumeration." },
                { DefaultSizeError, "Size is invalid. It must be greater than or equal to zero." },
                { InvalidProperty, "Invalid value {1} for property {0}." },
                { PathSyntax, "Path syntax is invalid." },
                { InvalidParameter, "Invalid value {1} for parameter {0}." },
                { WinNTRequired, "Feature requires Windows NT" },
                { TooManyColumns, "Column Count limit exceeded ({0})." },
                { MissingProperty, "Property {0} was not retrieved when receiving the message. Ensure that the PropertyFilter is set correctly." },
                { InvalidTrustee, "Trustee property of an entry in the access control list is null." },
                { InvalidTrusteeName, "Entry in the access control list contains a trustee with an invalid name." },
                { ArrivedTimeNotSet, "Arrived time is undefined for this message. This message was not created by a call to the Receive method." },
                { CriteriaNotDefined, "Criteria property has not been defined." },
                { InvalidDateValue, "Date is invalid. It must be between {0} and {1}." },
                { InvalidQueuePathToCreate, "Cannot create a queue with the path {0}." },
                { AsyncResultInvalid, "IAsyncResult interface passed is not valid because it was not created as a result of an asynchronous request on this object." },
                { QueueExistsError, "Cannot determine whether a queue with the specified format name exists." },
                { AmbiguousLabel, "Label \"{0}\" references more than one queue. Set the path for the desired queue." },
                { InvalidLabel, "Cannot find queue with label {0}." },
                { LongQueueName, "Queue name is too long. Size of queue name cannot exceed 255 characters." },
                { CouldntResolve, "Could not resolve name {0} (error = {1} )." },
                { AuthenticationNotSet, "Cannot determine authentication for this message. This message was not created by a call to the Receive method." },
                { NoCurrentMessageQueue, "Cursor is not currently pointing to a MessageQueue instance. It is located either before the first or after the last queue in the enumeration." },
                { TransactionNotStarted, "Cannot commit or roll back transaction because BeginTransaction has not been called." },
                { TypeListMissing, "Target type array is missing. The target type array must be set in order to deserialize the XML-formatted message." },
                { CouldntResolveName, "Could not resolve name {0}." },
                { FormatterMissing, "Cannot find a formatter capable of reading this message." },
                { DestinationQueueNotSet, "Destination queue is not defined for this message. The message was not created by a call to the Receive method." },
                { IdNotSet, "Unique identifier for this message is not defined. The message was not created by a call to the Receive method." },
                { MessageTypeNotSet, "Type is not defined for this message. The message was not created by a call to the Receive method." },
                { LookupIdNotSet, "Lookup identifier is not defined for this message. The message was not created by a call to the Receive method, or lookup identifier was not added to the properties to retrieve." },
                { SenderIdNotSet, "Sender identifier is not defined for this message. The message was not created by a call to the Receive method." },
                { VersionNotSet, "Message Queuing version is not defined for this message. The message was not created by a call to the Receive method." },
                { SentTimeNotSet, "Sent time is not defined for this message. The message was not created by a call to the Receive method." },
                { SourceMachineNotSet, "Source computer is not defined for this message. The message was not created by a call to the Receive method." },
                { InvalidId, "Identifier is not in the incorrect format." },
                { TransactionStarted, "Cannot start a transaction while a pending transaction exists." },
                { InvalidTypeDeserialization, "Cannot deserialize the message passed as an argument. Cannot recognize the serialization format." },
                { MessageNotFound, "Message requested was not found in the queue specified." },
                { UnknownError, "Error 0x{0} is unknown." },
                { "C00E0002", "One or more of the passed properties are invalid." },
                { "C00E0003", "The queue does not exist or you do not have sufficient permissions to perform the operation." },
                { "C00E0005", "A queue with the same path name already exists." },
                { "C00E0006", "Invalid parameter was passed to a function." },
                { "C00E0007", "An invalid handle was passed to the function." },
                { "C00E0008", "Operation was cancelled before it could be completed." },
                { "C00E0009", "Sharing violation resulted from queue being open already for exclusive receive." },
                { "C00E000B", "Message Queue service is not available." },
                { "C00E000D", "Cannot find specified machine." },
                { "C00E0010", "Sort specified in MQLocateBegin is invalid. There may be duplicate columns." },
                { "C00E0011", "User is invalid." },
                { "C00E0013", "A connection with Active Directory cannot be established. Verify that there are sufficient permissions to perform this operation." },
                { "C00E0014", "Invalid queue path name." },
                { "C00E0018", "Invalid property value." },
                { "C00E0019", "Invalid VARTYPE value." },
                { "C00E001A", "Buffer supplied to MQReceiveMessage for reading the message body was too small. The message cannot be removed from the queue, and the message body was truncated to fit the buffer when it was copied to the buffer." },
                { "C00E001B", "Timeout for the requested operation has expired." },
                { "C00E001C", "MQ_ACTION_PEEK_NEXT specified to MQReceiveMessage cannot be used with the current cursor position." },
                { "C00E001D", "Message that the cursor is currently pointing to has been removed from the queue by another process or by another call to Receive without the use of this cursor." },
                { "C00E001E", "Format name is invalid." },
                { "C00E001F", "Format name buffer supplied to the API was too small to fit the format name." },
                { "C00E0020", "The specified format name does not support the requested operation. For example, a direct queue format name cannot be deleted." },
                { "C00E0021", "Security descriptor is not valid." },
                { "C00E0022", "The buffer supplied for the user identifier property is too small." },
                { "C00E0023", "The buffer passed to MQGetQueueSecurity is too small." },
                { "C00E0024", "Security credentials cannot be verified because the Remote Procedure Call (RPC) server cannot reproduce the client application." },
                { "C00E0025", "Access to Message Queuing system is denied." },
                { "C00E0026", "Client does not have the required privileges to perform the operation." },
                { "C00E0027", "Insufficient resources to perform operation." },
                { "C00E0028", "Cannot process request because the user buffer is too small to hold the information returned by the request." },
                { "C00E002A", "Cannot store recoverable or journal message. The corresponding message was not sent." },
                { "C00E002B", "The buffer supplied for the user certificate property is too small." },
                { "C00E002C", "User certificate is not valid." },
                { "C00E002D", "Internal Message Queuing certificate is corrupted." },
                { "C00E002F", "User's internal Message Queuing certificate does not exist." },
                { "C00E0030", "Cryptographic function has failed." },
                { "C00E0033", "Encryption operations are not supported by the computer." },
                { "C00E0035", "Security context is unrecognized." },
                { "C00E0036", "Cannot retrieve SID information from the thread token." },
                { "C00E0037", "Cannot retrieve user account information." },
                { "C00E0038", "MQCOLUMNS parameter is invalid." },
                { "C00E0039", "Invalid propid value." },
                { "C00E003A", "Invalid relation value in restriction." },
                { "C00E003B", "Invalid property buffer size." },
                { "C00E003C", "Invalid propid value in MQRESTRICTION parameter." },
                { "C00E003D", "Invalid MQQUEUEPROPS parameter. It is either null or has zero properties." },
                { "C00E003E", "Invalid propid supplied for the requested Operation." },
                { "C00E003F", "Required properties for the operation were not all specified in the input parameters." },
                { "C00E0040", "The MSMQ Configuration (msmq) object already exists in Active Directory." },
                { "C00E0041", "Invalid MQQMPROPS parameter. It is either null or has zero properties." },
                { "C00E0042", "DS is full." },
                { "C00E0043", "Internal DS error has occurred." },
                { "C00E0044", "Object owner was invalid. For example, MQCreateQueue failed because the Queue Manager object is invalid." },
                { "C00E0045", "The specified access mode is not supported." },
                { "C00E0046", "The buffer supplied for the result is too small." },
                { "C00E0048", "Connected Network is in use and cannot be deleted." },
                { "C00E0049", "The object owner has not responded." },
                { "C00E004A", "The object owner is not reachable." },
                { "C00E004B", "Error occurs when reading from a queue on a remote computer." },
                { "C00E004C", "Failure to connect to MS DTC." },
                { "C00E004E", "Cannot import the transaction." },
                { "C00E0050", "The transaction usage is invalid." },
                { "C00E0051", "The transaction operations sequence is invalid." },
                { "C00E0055", "ConnectorType was not supplied. The connector type is required to send acknowledgment or secure messages." },
                { "C00E0056", "The Queue manager service has been restarted. The queue handle is stale and should be closed." },
                { "C00E0058", "Cannot enlist the transaction." },
                { "C00E005A", "Queue handle can no longer be used to receive messages because the queue was deleted. The handle should be closed." },
                { "C00E005B", "Invalid context Parameter(MQLocateBegin)." },
                { "C00E005C", "Invalid propid value in MQSORTSET." },
                { "C00E005D", "Label size is too big. It should be less than or equal to MQ_MAX_MSG_LABEL_LEN." },
                { "C00E005E", "Label buffer supplied to the API was too small." },
                { "C00E005F", "Registry list of MQIS servers is empty." },
                { "C00E0060", "MQIS database is in read-only mode." },
                { "C00E0061", "The buffer supplied for the symmetric key property is too small." },
                { "C00E0062", "The buffer supplied for the signature property is too small." },
                { "C00E0063", "The buffer passed for the Provider name property is too small." },
                { "C00E0064", "Foreign message queuing system does not support the operation." },
                { "C00E0065", "The database does not currently allow write operations because another MQIS server is being installed." },
                { "C00E0066", "MSMQ independent clients cannot serve MSMQ dependent clients." },
                { "C00E0067", "Message Queuing server has reached the maximum number of dependent clients it can serve." },
                { "C00E0068", "Initialization file for queue {1} in LQS was deleted because it was corrupted." },
                { "C00E0069", "Remote computer is not available." },
                { "C00E006A", "A workgroup installation computer does not support the operation." },
                { "C00E006B", "The Cryptographic Service Provider is not supported by Message Queuing." },
                { "C00E006C", "Cannot set the Security descriptor for the cryptographic keys." },
                { "C00E006D", "A user attempted to send an authenticated message without a certificate." },
                { "C00E006E", "Column PROPID_Q_PATHNAME_DNS is not supported for the MQLocateBegin API." },
                { "C00E006F", "Cannot create a certificate store for the internal certificate." },
                { "C00E0070", "Cannot open a certificate store for the internal certificate." },
                { "C00E0071", "MSMQServices object does not support the operation." },
                { "C00E0072", "Failed to grant the Add Guid permission to the current user." },
                { "C00E0073", "MSMQOCM.DLL library cannot be loaded." },
                { "C00E0074", "Entry point in the MSMQOCM.DLL library cannot be located." },
                { "C00E0075", "Failed to find Message Queuing servers on the domain controllers." },
                { "C00E0076", "The computer joined the domain, but Message Queuing will continue to run in workgroup mode because it failed to register itself in Active Directory." },
                { "C00E0077", "Failed to create an object on a specified Global Catalog server." },
                { "C00E0078", "Failed to create msmqConfiguration object with GUID that matched computer installation. You must uninstall Message Queuing and then reinstall it." },
                { "C00E0079", "Public key for the computer cannot be found." },
                { "C00E007A", "Public key for the computer does not exist." },
                { "C00E007B", "MQPRIVATEPROPS parameter value is invalid. This might be because it has a null value or has zero properties specified." },
                { "C00E007C", "Cannot find Global Catalog servers in the specified domain." },
                { "C00E007D", "Cannot find Message Queuing servers on the Global Catalog domain controllers." },
                { "C00E007E", "Failed to retrieve the distinguished name of the local computer." },
                { "C00E007F", "Cannot hash Data for an authenticated message." },
                { "C00E0080", "Cannot sign data for an authenticated message before it was sent." },
                { "C00E0081", "Cannot create hash object for an authenticated message." },
                { "C00E0082", "Signature of a received message is not valid." }
            };

            resources = FrozenDictionary.ToFrozenDictionary(data);
        }

        public static string GetString(string key) => resources[key];

        public static string GetString(string key, object input0) => string.Format(resources[key], input0);

        public static string GetString(string key, object input0, object input1) => string.Format(resources[key], input0, input1);

        public const string MSMQNotInstalled = nameof(MSMQNotInstalled);
        public const string PlatformNotSupported = nameof(PlatformNotSupported);
        public const string MSMQInfoNotSupported = nameof(MSMQInfoNotSupported);
        public const string NotAcknowledgement = nameof(NotAcknowledgement);
        public const string NoCurrentMessage = nameof(NoCurrentMessage);
        public const string DefaultSizeError = nameof(DefaultSizeError);
        public const string InvalidProperty = nameof(InvalidProperty);
        public const string PathSyntax = nameof(PathSyntax);
        public const string InvalidParameter = nameof(InvalidParameter);
        public const string WinNTRequired = nameof(WinNTRequired);
        public const string TooManyColumns = nameof(TooManyColumns);
        public const string MissingProperty = nameof(MissingProperty);
        public const string InvalidTrustee = nameof(InvalidTrustee);
        public const string InvalidTrusteeName = nameof(InvalidTrusteeName);
        public const string ArrivedTimeNotSet = nameof(ArrivedTimeNotSet);
        public const string CriteriaNotDefined = nameof(CriteriaNotDefined);
        public const string InvalidDateValue = nameof(InvalidDateValue);
        public const string InvalidQueuePathToCreate = nameof(InvalidQueuePathToCreate);
        public const string AsyncResultInvalid = nameof(AsyncResultInvalid);
        public const string QueueExistsError = nameof(QueueExistsError);
        public const string AmbiguousLabel = nameof(AmbiguousLabel);
        public const string InvalidLabel = nameof(InvalidLabel);
        public const string LongQueueName = nameof(LongQueueName);
        public const string CouldntResolve = nameof(CouldntResolve);
        public const string AuthenticationNotSet = nameof(AuthenticationNotSet);
        public const string NoCurrentMessageQueue = nameof(NoCurrentMessageQueue);
        public const string TransactionNotStarted = nameof(TransactionNotStarted);
        public const string TypeListMissing = nameof(TypeListMissing);
        public const string CouldntResolveName = nameof(CouldntResolveName);
        public const string FormatterMissing = nameof(FormatterMissing);
        public const string DestinationQueueNotSet = nameof(DestinationQueueNotSet);
        public const string IdNotSet = nameof(IdNotSet);
        public const string MessageTypeNotSet = nameof(MessageTypeNotSet);
        public const string LookupIdNotSet = nameof(LookupIdNotSet);
        public const string SenderIdNotSet = nameof(SenderIdNotSet);
        public const string VersionNotSet = nameof(VersionNotSet);
        public const string SentTimeNotSet = nameof(SentTimeNotSet);
        public const string SourceMachineNotSet = nameof(SourceMachineNotSet);
        public const string InvalidId = nameof(InvalidId);
        public const string TransactionStarted = nameof(TransactionStarted);
        public const string InvalidTypeDeserialization = nameof(InvalidTypeDeserialization);
        public const string MessageNotFound = nameof(MessageNotFound);
        public const string UnknownError = nameof(UnknownError);
    }
}

