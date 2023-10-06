//------------------------------------------------------------------------------
// <copyright file="Message.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------


namespace Messaging.Msmq
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using Messaging.Msmq.Interop;

    /// <include file='doc\Message.uex' path='docs/doc[@for="Message"]/*' />
    /// <devdoc>
    ///    <para>
    ///       Provides access to the properties needed to define a
    ///       Message Queuing message.
    ///    </para>
    /// </devdoc>
    public class Message : Component
    {
        const int GenericIdSize = 16;
        const int MessageIdSize = 20;
        const int DefaultQueueNameSize = 255;
        const int DefaultCryptographicProviderNameSize = 255;
        const int DefaultDigitalSignatureSize = 255;
        const int DefaultSenderCertificateSize = 255;
        const int DefaultSenderIdSize = 255;
        const int DefaultSymmetricKeySize = 255;
        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.InfiniteTimeout"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Specifies that there is no timeout.
        ///    </para>
        /// </devdoc>
        public static readonly TimeSpan InfiniteTimeout = TimeSpan.FromSeconds(uint.MaxValue);

        readonly MessagePropertyFilter filter;
        string machineName;
        bool receiveCreated;
        object cachedBodyObject;
        Stream cachedBodyStream;
        IMessageFormatter cachedFormatter;
        MessageQueue cachedResponseQueue;
        MessageQueue cachedTransactionStatusQueue;
        MessageQueue cachedAdminQueue;
        MessageQueue cachedDestinationQueue;
        internal MessagePropertyVariants properties;

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.Message"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Initializes a new instance of the <see cref='System.Messaging.Message'/> class with an empty body.
        ///    </para>
        /// </devdoc>
        public Message()
        {
            properties = new MessagePropertyVariants();
            receiveCreated = false;
            filter = new MessagePropertyFilter();

            //Always add Id
            properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_MSGID, new byte[MessageIdSize]);
            filter.Id = true;
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.Message1"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Initializes a new instance of the <see cref='System.Messaging.Message'/>
        ///       class, serializing the object passed as
        ///       an argument.
        ///    </para>
        /// </devdoc>
        public Message(object body)
            : this()
        {
            Body = body;
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.Message2"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public Message(object body, IMessageFormatter formatter)
            : this()
        {
            Formatter = formatter;
            Body = body;
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.Message3"]/*' />
        /// <internalonly/>
        internal Message(MessagePropertyFilter filter)
        {
            properties = new MessagePropertyVariants();
            receiveCreated = true;
            this.filter = filter;
            if (filter.data1 != 0)
            {
                int data = filter.data1;

                if (0 != (data & MessagePropertyFilter.ACKNOWLEDGEMENT))
                {
                    properties.SetUI2(NativeMethods.MESSAGE_PROPID_CLASS, 0);
                }

                if (0 != (data & MessagePropertyFilter.ACKNOWLEDGE_TYPE))
                {
                    properties.SetUI1(NativeMethods.MESSAGE_PROPID_ACKNOWLEDGE, 0);
                }

                if (0 != (data & MessagePropertyFilter.ADMIN_QUEUE))
                {
                    properties.SetString(NativeMethods.MESSAGE_PROPID_ADMIN_QUEUE, new byte[DefaultQueueNameSize * 2]);
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_ADMIN_QUEUE_LEN, DefaultQueueNameSize);
                }
                if (0 != (data & MessagePropertyFilter.BODY))
                {
                    properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_BODY, new byte[filter.bodySize]);
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_BODY_SIZE, filter.bodySize);
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_BODY_TYPE, 0);
                }
                if (0 != (data & MessagePropertyFilter.LABEL))
                {
                    properties.SetString(NativeMethods.MESSAGE_PROPID_LABEL, new byte[filter.labelSize * 2]);
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_LABEL_LEN, filter.labelSize);
                }
                if (0 != (data & MessagePropertyFilter.ID))
                {
                    properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_MSGID, new byte[MessageIdSize]);
                }

                if (0 != (data & MessagePropertyFilter.LOOKUP_ID))
                {
                    properties.SetUI8(NativeMethods.MESSAGE_PROPID_LOOKUPID, 0);
                }

                if (0 != (data & MessagePropertyFilter.USE_DEADLETTER_QUEUE))
                {
                    properties.SetUI1(NativeMethods.MESSAGE_PROPID_JOURNAL, 0);
                }

                if (0 != (data & MessagePropertyFilter.RESPONSE_QUEUE))
                {
                    properties.SetString(NativeMethods.MESSAGE_PROPID_RESP_QUEUE, new byte[DefaultQueueNameSize * 2]);
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_RESP_QUEUE_LEN, DefaultQueueNameSize);
                }
                //Acknowledgment and MessageType are overloaded in MQ.
                if ((0 == (data & MessagePropertyFilter.ACKNOWLEDGEMENT)) && (0 != (data & MessagePropertyFilter.MESSAGE_TYPE)))
                {
                    properties.SetUI2(NativeMethods.MESSAGE_PROPID_CLASS, 0);
                }

                //Journaling and Deadletter are overloaded in MSMQ
                if ((0 == (data & MessagePropertyFilter.USE_DEADLETTER_QUEUE)) && (0 != (data & MessagePropertyFilter.USE_JOURNALING)))
                {
                    properties.SetUI1(NativeMethods.MESSAGE_PROPID_JOURNAL, 0);
                }
            }

            if (filter.data2 != 0)
            {
                int data = filter.data2;
                if (0 != (data & MessagePropertyFilter.APP_SPECIFIC))
                {
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_APPSPECIFIC, 0);
                }

                if (0 != (data & MessagePropertyFilter.ARRIVED_TIME))
                {
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_ARRIVEDTIME, 0);
                }

                if (0 != (data & MessagePropertyFilter.ATTACH_SENDER_ID))
                {
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_SENDERID_TYPE, 0);
                }

                if (0 != (data & MessagePropertyFilter.AUTHENTICATED))
                {
                    properties.SetUI1(NativeMethods.MESSAGE_PROPID_AUTHENTICATED, 0);
                }

                if (0 != (data & MessagePropertyFilter.CONNECTOR_TYPE))
                {
                    properties.SetGuid(NativeMethods.MESSAGE_PROPID_CONNECTOR_TYPE, new byte[GenericIdSize]);
                }

                if (0 != (data & MessagePropertyFilter.CORRELATION_ID))
                {
                    properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_CORRELATIONID, new byte[MessageIdSize]);
                }

                if (0 != (data & MessagePropertyFilter.CRYPTOGRAPHIC_PROVIDER_NAME))
                {
                    properties.SetString(NativeMethods.MESSAGE_PROPID_PROV_NAME, new byte[DefaultCryptographicProviderNameSize * 2]);
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_PROV_NAME_LEN, DefaultCryptographicProviderNameSize);
                }
                if (0 != (data & MessagePropertyFilter.CRYPTOGRAPHIC_PROVIDER_TYPE))
                {
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_PROV_TYPE, 0);
                }

                if (0 != (data & MessagePropertyFilter.IS_RECOVERABLE))
                {
                    properties.SetUI1(NativeMethods.MESSAGE_PROPID_DELIVERY, 0);
                }

                if (0 != (data & MessagePropertyFilter.DESTINATION_QUEUE))
                {
                    properties.SetString(NativeMethods.MESSAGE_PROPID_DEST_QUEUE, new byte[DefaultQueueNameSize * 2]);
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_DEST_QUEUE_LEN, DefaultQueueNameSize);
                }
                if (0 != (data & MessagePropertyFilter.DIGITAL_SIGNATURE))
                {
                    properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_SIGNATURE, new byte[DefaultDigitalSignatureSize]);
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_SIGNATURE_LEN, DefaultDigitalSignatureSize);
                }
                if (0 != (data & MessagePropertyFilter.ENCRYPTION_ALGORITHM))
                {
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_ENCRYPTION_ALG, 0);
                }

                if (0 != (data & MessagePropertyFilter.EXTENSION))
                {
                    properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_EXTENSION, new byte[filter.extensionSize]);
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_EXTENSION_LEN, filter.extensionSize);
                }
                if (0 != (data & MessagePropertyFilter.FOREIGN_ADMIN_QUEUE))
                {
                    properties.SetString(NativeMethods.MESSAGE_PROPID_XACT_STATUS_QUEUE, new byte[DefaultQueueNameSize * 2]);
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_XACT_STATUS_QUEUE_LEN, DefaultQueueNameSize);
                }
                if (0 != (data & MessagePropertyFilter.HASH_ALGORITHM))
                {
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_HASH_ALG, 0);
                }

                if (0 != (data & MessagePropertyFilter.IS_FIRST_IN_TRANSACTION))
                {
                    properties.SetUI1(NativeMethods.MESSAGE_PROPID_FIRST_IN_XACT, 0);
                }

                if (0 != (data & MessagePropertyFilter.IS_LAST_IN_TRANSACTION))
                {
                    properties.SetUI1(NativeMethods.MESSAGE_PROPID_LAST_IN_XACT, 0);
                }

                if (0 != (data & MessagePropertyFilter.PRIORITY))
                {
                    properties.SetUI1(NativeMethods.MESSAGE_PROPID_PRIORITY, 0);
                }

                if (0 != (data & MessagePropertyFilter.SENDER_CERTIFICATE))
                {
                    properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_SENDER_CERT, new byte[DefaultSenderCertificateSize]);
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_SENDER_CERT_LEN, DefaultSenderCertificateSize);
                }
                if (0 != (data & MessagePropertyFilter.SENDER_ID))
                {
                    properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_SENDERID, new byte[DefaultSenderIdSize]);
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_SENDERID_LEN, DefaultSenderIdSize);
                }
                if (0 != (data & MessagePropertyFilter.SENT_TIME))
                {
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_SENTTIME, 0);
                }

                if (0 != (data & MessagePropertyFilter.SOURCE_MACHINE))
                {
                    properties.SetGuid(NativeMethods.MESSAGE_PROPID_SRC_MACHINE_ID, new byte[GenericIdSize]);
                }

                if (0 != (data & MessagePropertyFilter.SYMMETRIC_KEY))
                {
                    properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_DEST_SYMM_KEY, new byte[DefaultSymmetricKeySize]);
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_DEST_SYMM_KEY_LEN, DefaultSymmetricKeySize);
                }
                if (0 != (data & MessagePropertyFilter.TIME_TO_BE_RECEIVED))
                {
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_TIME_TO_BE_RECEIVED, 0);
                }

                if (0 != (data & MessagePropertyFilter.TIME_TO_REACH_QUEUE))
                {
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_TIME_TO_REACH_QUEUE, 0);
                }

                if (0 != (data & MessagePropertyFilter.TRANSACTION_ID))
                {
                    properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_XACTID, new byte[MessageIdSize]);
                }

                if (0 != (data & MessagePropertyFilter.USE_AUTHENTICATION))
                {
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_AUTH_LEVEL, 0);
                }

                if (0 != (data & MessagePropertyFilter.USE_ENCRYPTION))
                {
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_PRIV_LEVEL, 0);
                }

                if (0 != (data & MessagePropertyFilter.USE_TRACING))
                {
                    properties.SetUI1(NativeMethods.MESSAGE_PROPID_TRACE, 0);
                }

                if (0 != (data & MessagePropertyFilter.VERSION))
                {
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_VERSION, 0);
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.Acknowledgment"]/*' />
        /// <devdoc>
        ///    <para>Gets the classification
        ///       of acknowledgment messages that Message Queuing posts.</para>
        /// </devdoc>
        public Acknowledgment Acknowledgment
        {
            get
            {
                if (!filter.Acknowledgment)
                {
                    //This message cannot be an acknowledgment, because it has not been sent.
                    if (!receiveCreated)
                    {
                        throw new InvalidOperationException(Res.GetString(Res.NotAcknowledgement));
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "Acknowledgment"));
                }

                //Casting unsigned short to int, mask off sign extension.
                int res = properties.GetUI2(NativeMethods.MESSAGE_PROPID_CLASS) & 0x0000FFFF;
                return (Acknowledgment)res;
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.AcknowledgeType"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets the type of acknowledgment
        ///       message requested
        ///       from
        ///       Message Queuing when a message arrives in the queue.
        ///    </para>
        /// </devdoc>
        public AcknowledgeTypes AcknowledgeType
        {
            get
            {
                if (!filter.AcknowledgeType)
                {
                    //Return the default.
                    if (!receiveCreated)
                    {
                        return AcknowledgeTypes.None;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "AcknowledgeType"));
                }

                return (AcknowledgeTypes)properties.GetUI1(NativeMethods.MESSAGE_PROPID_ACKNOWLEDGE);
            }

            set
            {
                //If default
                if (value == AcknowledgeTypes.None)
                {
                    filter.AcknowledgeType = false;
                    properties.Remove(NativeMethods.MESSAGE_PROPID_ACKNOWLEDGE);
                }
                else
                {
                    filter.AcknowledgeType = true;
                    properties.SetUI1(NativeMethods.MESSAGE_PROPID_ACKNOWLEDGE, (byte)value);
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.AdministrationQueue"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets the queue used for acknowledgment
        ///       messages.
        ///    </para>
        /// </devdoc>
        public MessageQueue AdministrationQueue
        {
            get
            {
                if (!filter.AdministrationQueue)
                {
                    //This property has not been set, lets return an undefined value.
                    if (!receiveCreated)
                    {
                        return null;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "AdministrationQueue"));
                }

                if (cachedAdminQueue == null)
                {
                    if (properties.GetUI4(NativeMethods.MESSAGE_PROPID_ADMIN_QUEUE_LEN) != 0)
                    {
                        string queueFormatName = StringFromBytes(properties.GetString(NativeMethods.MESSAGE_PROPID_ADMIN_QUEUE),
                                                                 properties.GetUI4(NativeMethods.MESSAGE_PROPID_ADMIN_QUEUE_LEN));

                        cachedAdminQueue = new MessageQueue("FORMATNAME:" + queueFormatName);
                    }
                }

                return cachedAdminQueue;
            }

            set
            {
                if (value != null)
                {
                    filter.AdministrationQueue = true;
                }
                else
                {
                    //If default
                    if (filter.AdministrationQueue)
                    {
                        filter.AdministrationQueue = false;
                        properties.Remove(NativeMethods.MESSAGE_PROPID_ADMIN_QUEUE);
                        properties.Remove(NativeMethods.MESSAGE_PROPID_ADMIN_QUEUE_LEN);
                    }
                }

                cachedAdminQueue = value;
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.AppSpecific"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets
        ///       application-generated information regarding the message.
        ///    </para>
        /// </devdoc>
        public int AppSpecific
        {
            get
            {
                if (!filter.AppSpecific)
                {
                    //Return the default.
                    if (!receiveCreated)
                    {
                        return 0;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "AppSpecific"));
                }

                return properties.GetUI4(NativeMethods.MESSAGE_PROPID_APPSPECIFIC);
            }

            set
            {
                //If default
                if (value == 0)
                {
                    filter.AppSpecific = false;
                    properties.Remove(NativeMethods.MESSAGE_PROPID_APPSPECIFIC);
                }
                else
                {
                    filter.AppSpecific = true;
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_APPSPECIFIC, value);
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.ArrivedTime"]/*' />
        /// <devdoc>
        ///    Indicates when the message arrived at the queue.
        /// </devdoc>
        public DateTime ArrivedTime
        {
            get
            {
                if (!filter.ArrivedTime)
                {
                    //Undefined at this point, throw an exception.
                    if (!receiveCreated)
                    {
                        throw new InvalidOperationException(Res.GetString(Res.ArrivedTimeNotSet));
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "ArrivedTime"));
                }

                //Number of seconds ellapsed since 1/1/1970
                DateTime time = new(1970, 1, 1);
                time = time.AddSeconds(properties.GetUI4(NativeMethods.MESSAGE_PROPID_ARRIVEDTIME)).ToLocalTime();
                return time;
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.AttachSenderId"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets a value indicating whether the sender ID is to be attached
        ///       to the message.
        ///    </para>
        /// </devdoc>
        public bool AttachSenderId
        {
            get
            {
                if (!filter.AttachSenderId)
                {
                    //SenderId is attached by default.
                    if (!receiveCreated)
                    {
                        return true;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "AttachSenderId"));
                }

                int type = properties.GetUI4(NativeMethods.MESSAGE_PROPID_SENDERID_TYPE);
                if (type == NativeMethods.MESSAGE_SENDERID_TYPE_NONE)
                {
                    return false;
                }

                return true;
            }

            set
            {
                //If default.
                if (value)
                {
                    filter.AttachSenderId = false;
                    properties.Remove(NativeMethods.MESSAGE_PROPID_SENDERID_TYPE);
                }
                else
                {
                    filter.AttachSenderId = true;
                    if (value)
                    {
                        properties.SetUI4(NativeMethods.MESSAGE_PROPID_SENDERID_TYPE, NativeMethods.MESSAGE_SENDERID_TYPE_SID);
                    }
                    else
                    {
                        properties.SetUI4(NativeMethods.MESSAGE_PROPID_SENDERID_TYPE, NativeMethods.MESSAGE_SENDERID_TYPE_NONE);
                    }
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.Authenticated"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets a value indicating whether the message was
        ///       authenticated.
        ///    </para>
        /// </devdoc>
        public bool Authenticated
        {
            get
            {
                if (!filter.Authenticated)
                {
                    //Authentication is undefined, there is nothing to return here.
                    if (!receiveCreated)
                    {
                        throw new InvalidOperationException(Res.GetString(Res.AuthenticationNotSet));
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "Authenticated"));
                }

                return properties.GetUI1(NativeMethods.MESSAGE_PROPID_AUTHENTICATED) != 0;
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.AuthenticationProviderName"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets the name of the cryptographic
        ///       provider used to generate the digital signature of the message.
        ///    </para>
        /// </devdoc>
        public string AuthenticationProviderName
        {
            get
            {
                if (!filter.AuthenticationProviderName)
                {
                    //Return default
                    if (!receiveCreated)
                    {
                        return "Microsoft Base Cryptographic Provider, Ver. 1.0";
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "AuthenticationProviderName"));
                }

                if (properties.GetUI4(NativeMethods.MESSAGE_PROPID_PROV_NAME_LEN) != 0)
                {
                    return StringFromBytes(properties.GetString(NativeMethods.MESSAGE_PROPID_PROV_NAME),
                                           properties.GetUI4(NativeMethods.MESSAGE_PROPID_PROV_NAME_LEN));
                }
                else
                {
                    return "";
                }
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);

                //Should not remove if default, the default value might change in future MQ clients
                //if (value.CompareTo("Microsoft Base Cryptographic Provider, Ver. 1.0") == 0) {
                //    this.filter.AuthenticationProviderName = false;
                //    properties.Remove(NativeMethods.MESSAGE_PROPID_PROV_NAME);
                //    properties.Remove(NativeMethods.MESSAGE_PROPID_PROV_NAME_LEN);
                //}
                //else {
                filter.AuthenticationProviderName = true;
                properties.SetString(NativeMethods.MESSAGE_PROPID_PROV_NAME, StringToBytes(value));
                properties.SetUI4(NativeMethods.MESSAGE_PROPID_PROV_NAME_LEN, value.Length);
                //}
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.AuthenticationProviderType"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets the type of cryptographic provider used to
        ///       generate the digital signature of the
        ///       message.
        ///    </para>
        /// </devdoc>
        public CryptographicProviderType AuthenticationProviderType
        {
            get
            {
                //Return default
                if (!filter.AuthenticationProviderType)
                {
                    if (!receiveCreated)
                    {
                        return CryptographicProviderType.RsaFull;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "AuthenticationProviderType"));
                }

                return (CryptographicProviderType)properties.GetUI4(NativeMethods.MESSAGE_PROPID_PROV_TYPE);
            }

            set
            {
                if (!ValidationUtility.ValidateCryptographicProviderType(value))
                {
                    throw new InvalidEnumArgumentException("value", (int)value, typeof(CryptographicProviderType));
                }

                //Should not remove if default, the default value might change in future MQ clients
                //if (value == CryptographicProviderType.RsaFull) {
                //    this.filter.AuthenticationProviderType = false;
                //    properties.Remove(NativeMethods.MESSAGE_PROPID_PROV_TYPE);
                //}
                //else {
                filter.AuthenticationProviderType = true;
                properties.SetUI4(NativeMethods.MESSAGE_PROPID_PROV_TYPE, (int)value);
                //}
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.Body"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets
        ///       or sets the serialized
        ///       contents of the message.
        ///    </para>
        /// </devdoc>
        public object Body
        {
            get
            {
                if (!filter.Body)
                {
                    if (!receiveCreated)
                    {
                        return null;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "Body"));
                }

                if (cachedBodyObject == null)
                {
                    if (Formatter == null)
                    {
                        throw new InvalidOperationException(Res.GetString(Res.FormatterMissing));
                    }

                    cachedBodyObject = Formatter.Read(this);
                }

                return cachedBodyObject;
            }

            set
            {
                filter.Body = true;
                cachedBodyObject = value;
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.BodyStream"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets the information in the body of
        ///       the message.
        ///    </para>
        /// </devdoc>
        public Stream BodyStream
        {
            get
            {
                if (!filter.Body)
                {
                    if (!receiveCreated)
                    {
                        filter.Body = true;
                        cachedBodyStream ??= new MemoryStream();

                        return cachedBodyStream;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "Body"));
                }

                cachedBodyStream ??= new MemoryStream(properties.GetUI1Vector(NativeMethods.MESSAGE_PROPID_BODY),
                                                                                                0, properties.GetUI4(NativeMethods.MESSAGE_PROPID_BODY_SIZE));

                return cachedBodyStream;
            }

            set
            {
                if (value != null)
                {
                    filter.Body = true;
                }
                else
                {
                    filter.Body = false;
                    properties.Remove(NativeMethods.MESSAGE_PROPID_BODY);
                    properties.Remove(NativeMethods.MESSAGE_PROPID_BODY_TYPE);
                    properties.Remove(NativeMethods.MESSAGE_PROPID_BODY_SIZE);
                }

                cachedBodyStream = value;
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.BodyType"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets
        ///       or sets the type of data the message body contains.
        ///    </para>
        /// </devdoc>
        public int BodyType
        {
            get
            {
                if (!filter.Body)
                {
                    //Return default.
                    if (!receiveCreated)
                    {
                        return 0;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "Body"));
                }

                return properties.GetUI4(NativeMethods.MESSAGE_PROPID_BODY_TYPE);
            }

            set
            {
                properties.SetUI4(NativeMethods.MESSAGE_PROPID_BODY_TYPE, value);
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.ConnectorType"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Is required whenever an application sets a message property that is
        ///       typically set by MSMQ. It is typically used in the following two cases.
        ///       Whenever a message is passed by a connector application, the connector
        ///       type is required so that the sending and receiving applications know how
        ///       to interpret the security and acknowledgment properties of the messages.
        ///       When sending application-encrypted messages, this property tells the
        ///       MSMQ run time to use the symmetric key.
        ///    </para>
        /// </devdoc>
        public Guid ConnectorType
        {
            get
            {
                if (!filter.ConnectorType)
                {
                    //Return default.
                    if (!receiveCreated)
                    {
                        return Guid.Empty;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "ConnectorType"));
                }

                return new Guid(properties.GetGuid(NativeMethods.MESSAGE_PROPID_CONNECTOR_TYPE));
            }

            set
            {
                //If default
                if (value.Equals(Guid.Empty))
                {
                    filter.ConnectorType = false;
                    properties.Remove(NativeMethods.MESSAGE_PROPID_CONNECTOR_TYPE);
                }
                else
                {
                    filter.ConnectorType = true;
                    properties.SetGuid(NativeMethods.MESSAGE_PROPID_CONNECTOR_TYPE, value.ToByteArray());
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.CorrelationId"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets the message identifier used by
        ///       acknowledgment and report messages to reference the original
        ///       message.
        ///    </para>
        /// </devdoc>
        public string CorrelationId
        {
            get
            {
                if (!filter.CorrelationId)
                {
                    //Return default
                    if (!receiveCreated)
                    {
                        return string.Empty;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "CorrelationId"));
                }

                return IdFromByteArray(properties.GetUI1Vector(NativeMethods.MESSAGE_PROPID_CORRELATIONID));
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);

                //If default
                if (value.Length == 0)
                {
                    filter.CorrelationId = false;
                    properties.Remove(NativeMethods.MESSAGE_PROPID_CORRELATIONID);
                }
                else
                {
                    filter.CorrelationId = true;
                    properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_CORRELATIONID, IdToByteArray(value));
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.DefaultBodySize"]/*' />
        /// <devdoc>
        ///    The default body  buffer size to create,
        ///    when the message is received.
        /// </devdoc>
        int DefaultBodySize => filter.DefaultBodySize;

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.DefaultExtensionSize"]/*' />
        /// <devdoc>
        ///    The default extension  buffer size to create,
        ///    when the message is received.
        /// </devdoc>
        int DefaultExtensionSize => filter.DefaultExtensionSize;

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.DefaultLabelSize"]/*' />
        /// <devdoc>
        ///    The default label  buffer size to create,
        ///    when the message is received.
        /// </devdoc>
        int DefaultLabelSize => filter.DefaultLabelSize;

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.DestinationQueue"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Identifies the original destination queue for a message. It is typically
        ///       used to determine the original destination of a message that is in a journal
        ///       or dead-letter queue, however it can also be used when sending a
        ///       response message back to a response queue.
        ///    </para>
        /// </devdoc>
        public MessageQueue DestinationQueue
        {
            get
            {
                if (!filter.DestinationQueue)
                {
                    if (!receiveCreated)
                    {
                        throw new InvalidOperationException(Res.GetString(Res.DestinationQueueNotSet));
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "DestinationQueue"));
                }

                if (cachedDestinationQueue == null)
                {
                    if (properties.GetUI4(NativeMethods.MESSAGE_PROPID_DEST_QUEUE_LEN) != 0)
                    {
                        string queueFormatName = StringFromBytes(properties.GetString(NativeMethods.MESSAGE_PROPID_DEST_QUEUE),
                                                                 properties.GetUI4(NativeMethods.MESSAGE_PROPID_DEST_QUEUE_LEN));
                        cachedDestinationQueue = new MessageQueue("FORMATNAME:" + queueFormatName);
                    }
                }

                return cachedDestinationQueue;
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.DestinationSymmetricKey"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets
        ///       or sets the symmetric key used to encrypt messages.
        ///    </para>
        /// </devdoc>
        public byte[] DestinationSymmetricKey
        {
            get
            {
                if (!filter.DestinationSymmetricKey)
                {
                    if (!receiveCreated)
                    {
                        return [];
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "DestinationSymmetricKey"));
                }

                byte[] bytes = new byte[properties.GetUI4(NativeMethods.MESSAGE_PROPID_DEST_SYMM_KEY_LEN)];
                Array.Copy(properties.GetUI1Vector(NativeMethods.MESSAGE_PROPID_DEST_SYMM_KEY), bytes, bytes.Length);
                return bytes;
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);

                //If default
                if (value.Length == 0)
                {
                    filter.DestinationSymmetricKey = false;
                    properties.Remove(NativeMethods.MESSAGE_PROPID_DEST_SYMM_KEY);
                    properties.Remove(NativeMethods.MESSAGE_PROPID_DEST_SYMM_KEY_LEN);
                }
                else
                {
                    filter.DestinationSymmetricKey = true;
                    properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_DEST_SYMM_KEY, value);
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_DEST_SYMM_KEY_LEN, value.Length);
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.DigitalSignature"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or
        ///       sets the digital signature used to authenticate
        ///       the message.
        ///    </para>
        /// </devdoc>
        public byte[] DigitalSignature
        {
            get
            {
                if (!filter.DigitalSignature)
                {
                    if (!receiveCreated)
                    {
                        return [];
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "DigitalSignature"));
                }

                byte[] bytes = new byte[properties.GetUI4(NativeMethods.MESSAGE_PROPID_SIGNATURE_LEN)];
                Array.Copy(properties.GetUI1Vector(NativeMethods.MESSAGE_PROPID_SIGNATURE), bytes, bytes.Length);
                return bytes;
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);

                if (value.Length == 0)
                {
                    filter.DigitalSignature = false;
                    properties.Remove(NativeMethods.MESSAGE_PROPID_SIGNATURE);
                    properties.Remove(NativeMethods.MESSAGE_PROPID_SIGNATURE_LEN);
                }
                else
                {
                    filter.DigitalSignature = true;
                    filter.UseAuthentication = true;

                    properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_SIGNATURE, value);
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_SIGNATURE_LEN, value.Length);
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.EncryptionAlgorithm"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets the encryption algorithm used to encrypt the
        ///       body of a private message.
        ///    </para>
        /// </devdoc>
        public EncryptionAlgorithm EncryptionAlgorithm
        {
            get
            {
                if (!filter.EncryptionAlgorithm)
                {
                    //Return default.
                    if (!receiveCreated)
                    {
                        return EncryptionAlgorithm.Rc2;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "EncryptionAlgorithm"));
                }

                return (EncryptionAlgorithm)properties.GetUI4(NativeMethods.MESSAGE_PROPID_ENCRYPTION_ALG);
            }
            set
            {
                if (!ValidationUtility.ValidateEncryptionAlgorithm(value))
                {
                    throw new InvalidEnumArgumentException("value", (int)value, typeof(EncryptionAlgorithm));
                }

                //Should not remove if default, the default value might change in future MQ clients
                //if (value == EncryptionAlgorithm.Rc2) {
                //    this.filter.EncryptionAlgorithm = false;
                //    properties.Remove(NativeMethods.MESSAGE_PROPID_ENCRYPTION_ALG);
                //}
                //else {
                filter.EncryptionAlgorithm = true;
                properties.SetUI4(NativeMethods.MESSAGE_PROPID_ENCRYPTION_ALG, (int)value);
                //}
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.Extension"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets
        ///       additional information associated with the message.
        ///    </para>
        /// </devdoc>
        public byte[] Extension
        {
            get
            {
                if (!filter.Extension)
                {
                    //Return default.
                    if (!receiveCreated)
                    {
                        return [];
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "Extension"));
                }

                byte[] bytes = new byte[properties.GetUI4(NativeMethods.MESSAGE_PROPID_EXTENSION_LEN)];
                Array.Copy(properties.GetUI1Vector(NativeMethods.MESSAGE_PROPID_EXTENSION), bytes, bytes.Length);
                return bytes;
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);

                //If default
                if (value.Length == 0)
                {
                    filter.Extension = false;
                    properties.Remove(NativeMethods.MESSAGE_PROPID_EXTENSION);
                    properties.Remove(NativeMethods.MESSAGE_PROPID_EXTENSION_LEN);
                }
                else
                {
                    filter.Extension = true;
                    properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_EXTENSION, value);
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_EXTENSION_LEN, value.Length);
                }
            }
        }
        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.Formatter"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets
        ///       the formatter used to read or write an object into the message
        ///       body.
        ///    </para>
        /// </devdoc>
        public IMessageFormatter Formatter
        {
            get
            {
                return cachedFormatter;
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);

                cachedFormatter = value;
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.HashAlgorithm"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets the hashing
        ///       algorithm used when authenticating messages.
        ///    </para>
        /// </devdoc>
        public HashAlgorithm HashAlgorithm
        {
            get
            {
                if (!filter.HashAlgorithm)
                {
                    //This property has not been set, lets return an empty queue.
                    if (!receiveCreated)
                    {
                        return HashAlgorithm.Sha512;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "HashAlgorithm"));
                }

                return (HashAlgorithm)properties.GetUI4(NativeMethods.MESSAGE_PROPID_HASH_ALG);
            }

            set
            {
                if (!ValidationUtility.ValidateHashAlgorithm(value))
                {
                    throw new InvalidEnumArgumentException("value", (int)value, typeof(HashAlgorithm));
                }

                //Should not remove if default since MQ3.0 changed the default algorithm
                //if (value == HashAlgorithm.Md5) {
                //    this.filter.HashAlgorithm = false;
                //    properties.Remove(NativeMethods.MESSAGE_PROPID_HASH_ALG);
                //}
                //else {
                filter.HashAlgorithm = true;
                properties.SetUI4(NativeMethods.MESSAGE_PROPID_HASH_ALG, (int)value);
                //}
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.Id"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets
        ///       the Message Queuing-generated identifier of the message.
        ///    </para>
        /// </devdoc>
        public string Id
        {
            get
            {
                if (!filter.Id)
                {
                    //The Id is undefined at this point
                    if (!receiveCreated)
                    {
                        throw new InvalidOperationException(Res.GetString(Res.IdNotSet));
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "Id"));
                }

                return IdFromByteArray(properties.GetUI1Vector(NativeMethods.MESSAGE_PROPID_MSGID));
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.IsFirstInTransaction"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets a value indicating
        ///       whether the message was the first message sent in a transaction.
        ///    </para>
        /// </devdoc>
        public bool IsFirstInTransaction
        {
            get
            {
                if (!filter.IsFirstInTransaction)
                {
                    if (!receiveCreated)
                    {
                        return false;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "IsFirstInTransaction"));
                }

                return properties.GetUI1(NativeMethods.MESSAGE_PROPID_FIRST_IN_XACT) != 0;
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.IsLastInTransaction"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets a value indicating whether the message was
        ///       the last message sent in a transaction.
        ///    </para>
        /// </devdoc>
        public bool IsLastInTransaction
        {
            get
            {
                if (!filter.IsLastInTransaction)
                {
                    if (!receiveCreated)
                    {
                        return false;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "IsLastInTransaction"));
                }

                return properties.GetUI1(NativeMethods.MESSAGE_PROPID_LAST_IN_XACT) != 0;
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.Label"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets the message label.
        ///    </para>
        /// </devdoc>
        public string Label
        {
            get
            {
                if (!filter.Label)
                {
                    //Return default
                    if (!receiveCreated)
                    {
                        return string.Empty;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "Label"));
                }

                if (properties.GetUI4(NativeMethods.MESSAGE_PROPID_LABEL_LEN) != 0)
                {
                    return StringFromBytes(properties.GetString(NativeMethods.MESSAGE_PROPID_LABEL),
                                           properties.GetUI4(NativeMethods.MESSAGE_PROPID_LABEL_LEN));
                }
                else
                {
                    return "";
                }
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);

                //If default
                if (value.Length == 0)
                {
                    filter.Label = false;
                    properties.Remove(NativeMethods.MESSAGE_PROPID_LABEL);
                    properties.Remove(NativeMethods.MESSAGE_PROPID_LABEL_LEN);
                }
                else
                {
                    filter.Label = true;
                    properties.SetString(NativeMethods.MESSAGE_PROPID_LABEL, StringToBytes(value));
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_LABEL_LEN, value.Length);
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.LookupId"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets the Message Queue-generated lookup identifier of the message.
        ///    </para>
        /// </devdoc>
        public long LookupId
        {
            get
            {
                if (!MessageQueue.Msmq3OrNewer)
                {
                    throw new PlatformNotSupportedException(Res.GetString(Res.PlatformNotSupported));
                }

                if (!filter.LookupId)
                {
                    //Return default
                    if (!receiveCreated)
                    {
                        throw new InvalidOperationException(Res.GetString(Res.LookupIdNotSet));
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "LookupId"));
                }

                return properties.GetUI8(NativeMethods.MESSAGE_PROPID_LOOKUPID);
            }
        }

        internal void SetLookupId(long value)
        {
            filter.LookupId = true;
            properties.SetUI8(NativeMethods.MESSAGE_PROPID_LOOKUPID, value);
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.MessageType"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets the type of the message (normal, acknowledgment, or report).
        ///    </para>
        /// </devdoc>
        public MessageType MessageType
        {
            get
            {
                if (!filter.MessageType)
                {
                    //Return default
                    if (!receiveCreated)
                    {
                        throw new InvalidOperationException(Res.GetString(Res.MessageTypeNotSet));
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "MessageType"));
                }

                int cls = properties.GetUI2(NativeMethods.MESSAGE_PROPID_CLASS);
                if (cls == NativeMethods.MESSAGE_CLASS_NORMAL)
                {
                    return MessageType.Normal;
                }

                if (cls == NativeMethods.MESSAGE_CLASS_REPORT)
                {
                    return MessageType.Report;
                }

                return MessageType.Acknowledgment;
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.Priority"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets the message priority, used to determine
        ///       where the
        ///       message is placed in the
        ///       queue.
        ///    </para>
        /// </devdoc>
        public MessagePriority Priority
        {
            get
            {
                if (!filter.Priority)
                {
                    //Return default
                    if (!receiveCreated)
                    {
                        return MessagePriority.Normal;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "Priority"));
                }

                return (MessagePriority)properties.GetUI1(NativeMethods.MESSAGE_PROPID_PRIORITY);
            }

            set
            {
                if (!ValidationUtility.ValidateMessagePriority(value))
                {
                    throw new InvalidEnumArgumentException("value", (int)value, typeof(MessagePriority));
                }

                //If default
                if (value == MessagePriority.Normal)
                {
                    filter.Priority = false;
                    properties.Remove(NativeMethods.MESSAGE_PROPID_PRIORITY);
                }
                else
                {
                    filter.Priority = true;
                    properties.SetUI1(NativeMethods.MESSAGE_PROPID_PRIORITY, (byte)value);
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.Recoverable"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets a value
        ///       indicating whether the message is guaranteed to be delivered in the event of
        ///       a computer failure or network problem.
        ///    </para>
        /// </devdoc>
        public bool Recoverable
        {
            get
            {
                if (!filter.Recoverable)
                {
                    //Return default
                    if (!receiveCreated)
                    {
                        return false;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "Recoverable"));
                }

                return properties.GetUI1(NativeMethods.MESSAGE_PROPID_DELIVERY) == NativeMethods.MESSAGE_DELIVERY_RECOVERABLE;
            }

            set
            {
                //If default
                if (!value)
                {
                    filter.Recoverable = false;
                    properties.Remove(NativeMethods.MESSAGE_PROPID_DELIVERY);
                }
                else
                {
                    filter.Recoverable = true;
                    properties.SetUI1(NativeMethods.MESSAGE_PROPID_DELIVERY, NativeMethods.MESSAGE_DELIVERY_RECOVERABLE);
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.ResponseQueue"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets the queue which receives application-generated
        ///       response messages.
        ///    </para>
        /// </devdoc>
        public MessageQueue ResponseQueue
        {
            get
            {
                if (!filter.ResponseQueue)
                {
                    //This property has not been set, lets return an undefined value.
                    if (!receiveCreated)
                    {
                        return null;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "ResponseQueue"));
                }

                if (cachedResponseQueue == null)
                {
                    if (properties.GetUI4(NativeMethods.MESSAGE_PROPID_RESP_QUEUE_LEN) != 0)
                    {
                        string queueFormatName = StringFromBytes(properties.GetString(NativeMethods.MESSAGE_PROPID_RESP_QUEUE),
                                                                 properties.GetUI4(NativeMethods.MESSAGE_PROPID_RESP_QUEUE_LEN));

                        cachedResponseQueue = new MessageQueue("FORMATNAME:" + queueFormatName);
                    }
                }

                return cachedResponseQueue;
            }

            set
            {
                //If default
                if (value != null)
                {
                    filter.ResponseQueue = true;
                }
                else
                {
                    if (filter.ResponseQueue)
                    {
                        filter.ResponseQueue = false;
                        properties.Remove(NativeMethods.MESSAGE_PROPID_RESP_QUEUE);
                        properties.Remove(NativeMethods.MESSAGE_PROPID_RESP_QUEUE_LEN);
                    }
                }

                cachedResponseQueue = value;
            }
        }

        public SecurityContext SecurityContext
        {
            get
            {
                if (!filter.SecurityContext)
                {
                    return null;
                }

                IntPtr handle = properties.GetUI4(NativeMethods.MESSAGE_PROPID_SECURITY_CONTEXT);
                return new SecurityContext(new SecurityContextHandle(handle));
            }

            set
            {
                if (value == null)
                {
                    filter.SecurityContext = false;
                    properties.Remove(NativeMethods.MESSAGE_PROPID_SECURITY_CONTEXT);
                }
                else
                {
                    filter.SecurityContext = true;
                    // Can't store IntPtr because property type is UI4, but IntPtr can be 64 bits
                    int handle = value.Handle.DangerousGetHandle().ToInt32(); // this is safe because MSMQ always returns 32-bit handle
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_SECURITY_CONTEXT, handle);
                }

            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.SenderCertificate"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Specifies the security certificate used to authenticate messages.
        ///    </para>
        /// </devdoc>
        public byte[] SenderCertificate
        {
            get
            {
                if (!filter.SenderCertificate)
                {
                    //Return default
                    if (!receiveCreated)
                    {
                        return [];
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "SenderCertificate"));
                }

                byte[] bytes = new byte[properties.GetUI4(NativeMethods.MESSAGE_PROPID_SENDER_CERT_LEN)];
                Array.Copy(properties.GetUI1Vector(NativeMethods.MESSAGE_PROPID_SENDER_CERT), bytes, bytes.Length);
                return bytes;
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);

                //If default
                if (value.Length == 0)
                {
                    filter.SenderCertificate = false;
                    properties.Remove(NativeMethods.MESSAGE_PROPID_SENDER_CERT);
                    properties.Remove(NativeMethods.MESSAGE_PROPID_SENDER_CERT_LEN);
                }
                else
                {
                    filter.SenderCertificate = true;
                    properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_SENDER_CERT, value);
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_SENDER_CERT_LEN, value.Length);
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.SenderId"]/*' />
        /// <devdoc>
        ///    <para>
        ///       This property is set by MSMQ, and is used primarily by the
        ///       receiving Queue Manager when authenticating a message. The receiving
        ///       Queue Manager uses the sender identifier in this property to verify where
        ///       the message originated and to verify the sender has access rights to a queue.
        ///    </para>
        /// </devdoc>
        public byte[] SenderId
        {
            get
            {
                if (!filter.SenderId)
                {
                    if (!receiveCreated)
                    {
                        throw new InvalidOperationException(Res.GetString(Res.SenderIdNotSet));
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "SenderId"));
                }

                byte[] bytes = new byte[properties.GetUI4(NativeMethods.MESSAGE_PROPID_SENDERID_LEN)];
                Array.Copy(properties.GetUI1Vector(NativeMethods.MESSAGE_PROPID_SENDERID), bytes, bytes.Length);
                return bytes;
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.SenderVersion"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets the version of Message Queuing used to send the message.
        ///    </para>
        /// </devdoc>
        public long SenderVersion
        {
            get
            {
                if (!filter.SenderVersion)
                {
                    if (!receiveCreated)
                    {
                        throw new InvalidOperationException(Res.GetString(Res.VersionNotSet));
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "SenderVersion"));
                }

                return (uint)properties.GetUI4(NativeMethods.MESSAGE_PROPID_VERSION);
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.SentTime"]/*' />
        /// <devdoc>
        ///    Indicates the date and time that the message was sent by
        ///    the source Queue Manager.
        /// </devdoc>
        public DateTime SentTime
        {
            get
            {
                if (!filter.SentTime)
                {
                    if (!receiveCreated)
                    {
                        throw new InvalidOperationException(Res.GetString(Res.SentTimeNotSet));
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "SentTime"));
                }

                //Number of seconds ellapsed since 1/1/1970
                DateTime time = new(1970, 1, 1);
                time = time.AddSeconds(properties.GetUI4(NativeMethods.MESSAGE_PROPID_SENTTIME)).ToLocalTime();
                return time;
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.SourceMachine"]/*' />
        /// <devdoc>
        ///    Specifies the computer where the message originated.
        /// </devdoc>
        public string SourceMachine
        {
            get
            {
                if (!filter.SourceMachine)
                {
                    if (!receiveCreated)
                    {
                        throw new InvalidOperationException(Res.GetString(Res.SourceMachineNotSet));
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "SourceMachine"));
                }

                if (machineName == null)
                {
                    byte[] bytes = properties.GetGuid(NativeMethods.MESSAGE_PROPID_SRC_MACHINE_ID);
                    var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);

                    MachinePropertyVariants machineProperties = new();
                    machineProperties.SetNull(NativeMethods.MACHINE_PATHNAME);
                    int status = UnsafeNativeMethods.MQGetMachineProperties(null, handle.AddrOfPinnedObject(), machineProperties.Lock());
                    machineProperties.Unlock();
                    handle.Free();

                    IntPtr memoryHandle = machineProperties.GetIntPtr(NativeMethods.MACHINE_PATHNAME);
                    if (memoryHandle != 0)
                    {
                        //Using Unicode API even on Win9x
                        machineName = Marshal.PtrToStringUni(memoryHandle);
                        SafeNativeMethods.MQFreeMemory(memoryHandle);
                    }

                    if (MessageQueue.IsFatalError(status))
                    {
                        throw new MessageQueueException(status);
                    }
                }

                return machineName;
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.TimeToBeReceived"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or
        ///       sets the time limit for the message to be retrieved from the target
        ///       queue.
        ///    </para>
        /// </devdoc>
        public TimeSpan TimeToBeReceived
        {
            get
            {
                if (!filter.TimeToBeReceived)
                {
                    //Return default
                    if (!receiveCreated)
                    {
                        return InfiniteTimeout;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "TimeToBeReceived"));
                }

                return TimeSpan.FromSeconds((uint)properties.GetUI4(NativeMethods.MESSAGE_PROPID_TIME_TO_BE_RECEIVED));
            }

            set
            {
                long timeoutInSeconds = (long)value.TotalSeconds;
                if (timeoutInSeconds < 0)
                {
                    throw new ArgumentException(Res.GetString(Res.InvalidProperty, "TimeToBeReceived", value.ToString()));
                }

                if (timeoutInSeconds > uint.MaxValue)
                {
                    timeoutInSeconds = uint.MaxValue;
                }

                //If default
                if (timeoutInSeconds == uint.MaxValue)
                {
                    filter.TimeToBeReceived = false;
                    properties.Remove(NativeMethods.MESSAGE_PROPID_TIME_TO_BE_RECEIVED);
                }
                else
                {
                    filter.TimeToBeReceived = true;
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_TIME_TO_BE_RECEIVED, (int)(uint)timeoutInSeconds);
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.TimeToReachQueue"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets the time limit for the message to reach
        ///       the queue.
        ///    </para>
        /// </devdoc>
        public TimeSpan TimeToReachQueue
        {
            get
            {
                if (!filter.TimeToReachQueue)
                {
                    //Return default
                    if (!receiveCreated)
                    {
                        return InfiniteTimeout;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "TimeToReachQueue"));
                }

                return TimeSpan.FromSeconds((uint)properties.GetUI4(NativeMethods.MESSAGE_PROPID_TIME_TO_REACH_QUEUE));
            }

            set
            {
                long timeoutInSeconds = (long)value.TotalSeconds;
                if (timeoutInSeconds < 0)
                {
                    throw new ArgumentException(Res.GetString(Res.InvalidProperty, "TimeToReachQueue", value.ToString()));
                }

                if (timeoutInSeconds > uint.MaxValue)
                {
                    timeoutInSeconds = uint.MaxValue;
                }

                if (timeoutInSeconds == uint.MaxValue)
                {
                    filter.TimeToReachQueue = false;
                    properties.Remove(NativeMethods.MESSAGE_PROPID_TIME_TO_REACH_QUEUE);
                }
                else
                {
                    filter.TimeToReachQueue = true;
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_TIME_TO_REACH_QUEUE, (int)(uint)timeoutInSeconds);
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.TransactionId"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets the
        ///       identifier for the transaction of which the message was a part.
        ///    </para>
        /// </devdoc>
        public string TransactionId
        {
            get
            {
                if (!filter.TransactionId)
                {
                    //Return default
                    if (!receiveCreated)
                    {
                        return string.Empty;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "TransactionId"));
                }

                return IdFromByteArray(properties.GetUI1Vector(NativeMethods.MESSAGE_PROPID_XACTID));
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.TransactionStatusQueue"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets the
        ///       transaction status queue on the source computer.
        ///    </para>
        /// </devdoc>
        public MessageQueue TransactionStatusQueue
        {
            get
            {
                if (!filter.TransactionStatusQueue)
                {
                    //This property has not been set, lets return an undefined value.
                    if (!receiveCreated)
                    {
                        return null;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "TransactionStatusQueue"));
                }

                if (cachedTransactionStatusQueue == null)
                {
                    if (properties.GetUI4(NativeMethods.MESSAGE_PROPID_XACT_STATUS_QUEUE_LEN) != 0)
                    {
                        string queueFormatName = StringFromBytes(properties.GetString(NativeMethods.MESSAGE_PROPID_XACT_STATUS_QUEUE),
                                                                 properties.GetUI4(NativeMethods.MESSAGE_PROPID_XACT_STATUS_QUEUE_LEN));

                        cachedTransactionStatusQueue = new MessageQueue("FORMATNAME:" + queueFormatName);
                    }
                }

                return cachedTransactionStatusQueue;
            }

            set
            {
                //If default
                if (value != null)
                {
                    filter.TransactionStatusQueue = true;
                }
                else
                {
                    if (filter.TransactionStatusQueue)
                    {
                        filter.TransactionStatusQueue = false;
                        properties.Remove(NativeMethods.MESSAGE_PROPID_XACT_STATUS_QUEUE);
                        properties.Remove(NativeMethods.MESSAGE_PROPID_XACT_STATUS_QUEUE_LEN);
                    }
                }

                cachedTransactionStatusQueue = value;
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.UseAuthentication"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets
        ///       or sets a value indicating whether a message must be authenticated.
        ///    </para>
        /// </devdoc>
        public bool UseAuthentication
        {
            get
            {
                if (!filter.UseAuthentication)
                {
                    //Return default
                    if (!receiveCreated)
                    {

                        // Actually, we dont know what default is:
                        // Algorithm to determine whether or not messages
                        // should be authenticated by default is non-trivial
                        // and should not be reproduced in System.Messaging.
                        //
                        // One idea is to add a new native API,
                        // MQGetDefaultPropertyValue, to retrieve default values.
                        // (eugenesh, Nov 3 2004)
                        return false;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "UseAuthentication"));
                }

                return properties.GetUI4(NativeMethods.MESSAGE_PROPID_AUTH_LEVEL) != NativeMethods.MESSAGE_AUTHENTICATION_LEVEL_NONE;
            }

            set
            {
                //default is different on different versions of MSMQ,
                //so dont make any assumptions and explicitly pass what user requested
                filter.UseAuthentication = true;
                if (!value)
                {
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_AUTH_LEVEL, NativeMethods.MESSAGE_AUTHENTICATION_LEVEL_NONE);
                }
                else
                {
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_AUTH_LEVEL, NativeMethods.MESSAGE_AUTHENTICATION_LEVEL_ALWAYS);
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.UseDeadLetterQueue"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets a value indicating whether
        ///       a copy of an undeliverable message should be sent to a dead-letter queue.
        ///    </para>
        /// </devdoc>
        public bool UseDeadLetterQueue
        {
            get
            {
                if (!filter.UseDeadLetterQueue)
                {
                    //Return default
                    if (!receiveCreated)
                    {
                        return false;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "UseDeadLetterQueue"));
                }

                return (properties.GetUI1(NativeMethods.MESSAGE_PROPID_JOURNAL) & NativeMethods.MESSAGE_JOURNAL_DEADLETTER) != 0;
            }

            set
            {
                //If Default
                if (!value)
                {
                    if (filter.UseDeadLetterQueue)
                    {
                        filter.UseDeadLetterQueue = false;
                        if (!filter.UseJournalQueue)
                        {
                            properties.Remove(NativeMethods.MESSAGE_PROPID_JOURNAL);
                        }
                        else
                        {
                            properties.SetUI1(NativeMethods.MESSAGE_PROPID_JOURNAL, (byte)(properties.GetUI1(NativeMethods.MESSAGE_PROPID_JOURNAL) & ~NativeMethods.MESSAGE_JOURNAL_DEADLETTER));
                        }
                    }
                }
                else
                {
                    if (!filter.UseDeadLetterQueue && !filter.UseJournalQueue)
                    {
                        properties.SetUI1(NativeMethods.MESSAGE_PROPID_JOURNAL, NativeMethods.MESSAGE_JOURNAL_DEADLETTER);
                    }
                    else
                    {
                        properties.SetUI1(NativeMethods.MESSAGE_PROPID_JOURNAL, (byte)(properties.GetUI1(NativeMethods.MESSAGE_PROPID_JOURNAL) | NativeMethods.MESSAGE_JOURNAL_DEADLETTER));
                    }

                    filter.UseDeadLetterQueue = true;
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.UseEncryption"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets a value indicating whether to encrypt messages.
        ///    </para>
        /// </devdoc>
        public bool UseEncryption
        {
            get
            {
                if (!filter.UseEncryption)
                {
                    //Return default
                    if (!receiveCreated)
                    {
                        return false;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "UseEncryption"));
                }

                return properties.GetUI4(NativeMethods.MESSAGE_PROPID_PRIV_LEVEL) != NativeMethods.MESSAGE_PRIVACY_LEVEL_NONE;
            }

            set
            {
                //If default
                if (!value)
                {
                    filter.UseEncryption = false;
                    properties.Remove(NativeMethods.MESSAGE_PROPID_PRIV_LEVEL);
                }
                else
                {
                    filter.UseEncryption = true;
                    properties.SetUI4(NativeMethods.MESSAGE_PROPID_PRIV_LEVEL, NativeMethods.MESSAGE_PRIVACY_LEVEL_BODY);
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.UseJournalQueue"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets a value indicating whether a copy of the message should be kept in a machine
        ///       journal on the originating computer.
        ///    </para>
        /// </devdoc>
        public bool UseJournalQueue
        {
            get
            {
                if (!filter.UseJournalQueue)
                {
                    //Return default
                    if (!receiveCreated)
                    {
                        return false;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "UseJournalQueue"));
                }

                return (properties.GetUI1(NativeMethods.MESSAGE_PROPID_JOURNAL) & NativeMethods.MESSAGE_JOURNAL_JOURNAL) != 0;
            }

            set
            {
                //If Default
                if (!value)
                {
                    if (filter.UseJournalQueue)
                    {
                        filter.UseJournalQueue = false;
                        if (!filter.UseDeadLetterQueue)
                        {
                            properties.Remove(NativeMethods.MESSAGE_PROPID_JOURNAL);
                        }
                        else
                        {
                            properties.SetUI1(NativeMethods.MESSAGE_PROPID_JOURNAL, (byte)(properties.GetUI1(NativeMethods.MESSAGE_PROPID_JOURNAL) & ~NativeMethods.MESSAGE_JOURNAL_JOURNAL));
                        }
                    }
                }
                else
                {
                    if (!filter.UseDeadLetterQueue && !filter.UseJournalQueue)
                    {
                        properties.SetUI1(NativeMethods.MESSAGE_PROPID_JOURNAL, NativeMethods.MESSAGE_JOURNAL_JOURNAL);
                    }
                    else
                    {
                        properties.SetUI1(NativeMethods.MESSAGE_PROPID_JOURNAL, (byte)(properties.GetUI1(NativeMethods.MESSAGE_PROPID_JOURNAL) | NativeMethods.MESSAGE_JOURNAL_JOURNAL));
                    }

                    filter.UseJournalQueue = true;
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.UseTracing"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or
        ///       sets a value indicating whether to trace a message as
        ///       it moves toward its destination queue.
        ///    </para>
        /// </devdoc>
        public bool UseTracing
        {
            get
            {
                if (!filter.UseTracing)
                {
                    //Return default
                    if (!receiveCreated)
                    {
                        return false;
                    }

                    throw new InvalidOperationException(Res.GetString(Res.MissingProperty, "UseTracing"));
                }

                return properties.GetUI1(NativeMethods.MESSAGE_PROPID_TRACE) != NativeMethods.MESSAGE_TRACE_NONE;
            }

            set
            {
                //If Default
                if (!value)
                {
                    filter.UseTracing = false;
                    properties.Remove(NativeMethods.MESSAGE_PROPID_TRACE);
                }
                else
                {
                    filter.UseTracing = true;

                    if (!value)
                    {
                        properties.SetUI1(NativeMethods.MESSAGE_PROPID_TRACE, NativeMethods.MESSAGE_TRACE_NONE);
                    }
                    else
                    {
                        properties.SetUI1(NativeMethods.MESSAGE_PROPID_TRACE, NativeMethods.MESSAGE_TRACE_SEND_ROUTE_TO_REPORT_QUEUE);
                    }
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.AdjustMemory"]/*' />
        /// <internalonly/>
        internal void AdjustMemory()
        {
            if (filter.AdministrationQueue)
            {
                int size = properties.GetUI4(NativeMethods.MESSAGE_PROPID_ADMIN_QUEUE_LEN);
                if (size > DefaultQueueNameSize)
                {
                    properties.SetString(NativeMethods.MESSAGE_PROPID_ADMIN_QUEUE, new byte[size * 2]);
                }
            }

            if (filter.Body)
            {
                int size = properties.GetUI4(NativeMethods.MESSAGE_PROPID_BODY_SIZE);
                if (size > DefaultBodySize)
                {
                    properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_BODY, new byte[size]);
                }
            }

            if (filter.AuthenticationProviderName)
            {
                int size = properties.GetUI4(NativeMethods.MESSAGE_PROPID_PROV_NAME_LEN);
                if (size > DefaultCryptographicProviderNameSize)
                {
                    properties.SetString(NativeMethods.MESSAGE_PROPID_PROV_NAME, new byte[size * 2]);
                }
            }

            if (filter.DestinationQueue)
            {
                int size = properties.GetUI4(NativeMethods.MESSAGE_PROPID_DEST_QUEUE_LEN);
                if (size > DefaultQueueNameSize)
                {
                    properties.SetString(NativeMethods.MESSAGE_PROPID_DEST_QUEUE, new byte[size * 2]);
                }
            }

            if (filter.Extension)
            {
                int size = properties.GetUI4(NativeMethods.MESSAGE_PROPID_EXTENSION_LEN);
                if (size > DefaultExtensionSize)
                {
                    properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_EXTENSION, new byte[size]);
                }
            }

            if (filter.TransactionStatusQueue)
            {
                int size = properties.GetUI4(NativeMethods.MESSAGE_PROPID_XACT_STATUS_QUEUE_LEN);
                if (size > DefaultQueueNameSize)
                {
                    properties.SetString(NativeMethods.MESSAGE_PROPID_XACT_STATUS_QUEUE, new byte[size * 2]);
                }
            }

            if (filter.Label)
            {
                int size = properties.GetUI4(NativeMethods.MESSAGE_PROPID_LABEL_LEN);
                if (size > DefaultLabelSize)
                {
                    properties.SetString(NativeMethods.MESSAGE_PROPID_LABEL, new byte[size * 2]);
                }
            }

            if (filter.ResponseQueue)
            {
                int size = properties.GetUI4(NativeMethods.MESSAGE_PROPID_RESP_QUEUE_LEN);
                if (size > DefaultQueueNameSize)
                {
                    properties.SetString(NativeMethods.MESSAGE_PROPID_RESP_QUEUE, new byte[size * 2]);
                }
            }

            if (filter.SenderCertificate)
            {
                int size = properties.GetUI4(NativeMethods.MESSAGE_PROPID_SENDER_CERT_LEN);
                if (size > DefaultSenderCertificateSize)
                {
                    properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_SENDER_CERT, new byte[size]);
                }
            }

            if (filter.SenderId)
            {
                int size = properties.GetUI4(NativeMethods.MESSAGE_PROPID_SENDERID_LEN);
                if (size > DefaultSenderIdSize)
                {
                    properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_SENDERID, new byte[size]);
                }
            }

            if (filter.DestinationSymmetricKey)
            {
                int size = properties.GetUI4(NativeMethods.MESSAGE_PROPID_DEST_SYMM_KEY_LEN);
                if (size > DefaultSymmetricKeySize)
                {
                    properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_DEST_SYMM_KEY, new byte[size]);
                }
            }

            if (filter.DigitalSignature)
            {
                int size = properties.GetUI4(NativeMethods.MESSAGE_PROPID_SIGNATURE_LEN);
                if (size > DefaultDigitalSignatureSize)
                {
                    properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_SIGNATURE, new byte[size]);
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.AdjustToSend"]/*' />
        /// <internalonly/>
        internal void AdjustToSend()
        {
            //Write cached properties
            string queueFormatName;
            if (filter.AdministrationQueue && cachedAdminQueue != null)
            {
                queueFormatName = cachedAdminQueue.FormatName;
                properties.SetString(NativeMethods.MESSAGE_PROPID_ADMIN_QUEUE, StringToBytes(queueFormatName));
                properties.SetUI4(NativeMethods.MESSAGE_PROPID_ADMIN_QUEUE_LEN, queueFormatName.Length);
            }

            if (filter.ResponseQueue && cachedResponseQueue != null)
            {
                queueFormatName = cachedResponseQueue.FormatName;
                properties.SetString(NativeMethods.MESSAGE_PROPID_RESP_QUEUE, StringToBytes(queueFormatName));
                properties.SetUI4(NativeMethods.MESSAGE_PROPID_RESP_QUEUE_LEN, queueFormatName.Length);
            }

            if (filter.TransactionStatusQueue && cachedTransactionStatusQueue != null)
            {
                queueFormatName = cachedTransactionStatusQueue.FormatName;
                properties.SetString(NativeMethods.MESSAGE_PROPID_XACT_STATUS_QUEUE, StringToBytes(queueFormatName));
                properties.SetUI4(NativeMethods.MESSAGE_PROPID_XACT_STATUS_QUEUE_LEN, queueFormatName.Length);
            }

            if (filter.Body && cachedBodyObject != null)
            {
                Formatter ??= new XmlMessageFormatter();

                Formatter.Write(this, cachedBodyObject);
            }

            if (filter.Body && cachedBodyStream != null)
            {
                cachedBodyStream.Position = 0;
                byte[] bytes = new byte[(int)cachedBodyStream.Length];
                cachedBodyStream.Read(bytes, 0, bytes.Length);
                properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_BODY, bytes);
                properties.SetUI4(NativeMethods.MESSAGE_PROPID_BODY_SIZE, bytes.Length);
            }

            if (receiveCreated)
            {
                lock (this)
                {
                    if (receiveCreated)
                    {
                        //We don't want to send the buffers as they were allocated
                        //when receiving, they might be to big.
                        //Adjust sizes
                        if (filter.Body)
                        {
                            int bodySize = properties.GetUI4(NativeMethods.MESSAGE_PROPID_BODY_SIZE);
                            byte[] bodyArray = properties.GetUI1Vector(NativeMethods.MESSAGE_PROPID_BODY);

                            Debug.Assert(bodySize <= bodyArray.Length, "Allocated body array size is bigger than BODY_SIZE property");

                            if (bodySize < bodyArray.Length)
                            { // need to reallocate body array

                                byte[] bytes = new byte[bodySize];
                                Array.Copy(bodyArray, bytes, bodySize);

                                properties.SetUI1Vector(NativeMethods.MESSAGE_PROPID_BODY, bytes);
                            }
                        }
                        if (filter.Extension)
                        {
                            properties.AdjustSize(NativeMethods.MESSAGE_PROPID_EXTENSION,
                                                       properties.GetUI4(NativeMethods.MESSAGE_PROPID_EXTENSION_LEN));
                        }
                        if (filter.SenderCertificate)
                        {
                            properties.AdjustSize(NativeMethods.MESSAGE_PROPID_SENDER_CERT,
                                                       properties.GetUI4(NativeMethods.MESSAGE_PROPID_SENDER_CERT_LEN));
                        }
                        if (filter.DestinationSymmetricKey)
                        {
                            properties.AdjustSize(NativeMethods.MESSAGE_PROPID_DEST_SYMM_KEY,
                                                       properties.GetUI4(NativeMethods.MESSAGE_PROPID_DEST_SYMM_KEY_LEN));
                        }

                        //Ghost properties.
                        if (filter.Acknowledgment || filter.MessageType)
                        {
                            properties.Ghost(NativeMethods.MESSAGE_PROPID_CLASS);
                        }

                        if (filter.ArrivedTime)
                        {
                            properties.Ghost(NativeMethods.MESSAGE_PROPID_ARRIVEDTIME);
                        }

                        if (filter.Authenticated)
                        {
                            properties.Ghost(NativeMethods.MESSAGE_PROPID_AUTHENTICATED);
                        }

                        if (filter.DestinationQueue)
                        {
                            properties.Ghost(NativeMethods.MESSAGE_PROPID_DEST_QUEUE);
                            properties.Ghost(NativeMethods.MESSAGE_PROPID_DEST_QUEUE_LEN);
                            cachedDestinationQueue = null;
                        }
                        if (filter.IsFirstInTransaction)
                        {
                            properties.Ghost(NativeMethods.MESSAGE_PROPID_FIRST_IN_XACT);
                        }

                        if (filter.IsLastInTransaction)
                        {
                            properties.Ghost(NativeMethods.MESSAGE_PROPID_LAST_IN_XACT);
                        }

                        if (filter.SenderId)
                        {
                            properties.Ghost(NativeMethods.MESSAGE_PROPID_SENDERID);
                            properties.Ghost(NativeMethods.MESSAGE_PROPID_SENDERID_LEN);
                        }
                        if (filter.SentTime)
                        {
                            properties.Ghost(NativeMethods.MESSAGE_PROPID_SENTTIME);
                        }

                        if (filter.SourceMachine)
                        {
                            properties.Ghost(NativeMethods.MESSAGE_PROPID_SRC_MACHINE_ID);
                        }

                        if (filter.TransactionId)
                        {
                            properties.Ghost(NativeMethods.MESSAGE_PROPID_XACTID);
                        }

                        if (filter.SenderVersion)
                        {
                            properties.Ghost(NativeMethods.MESSAGE_PROPID_VERSION);
                        }

                        //Ghost invalid returned properties

                        if (filter.AdministrationQueue)
                        {
                            if (properties.GetUI4(NativeMethods.MESSAGE_PROPID_ADMIN_QUEUE_LEN) == 0)
                            {
                                properties.Ghost(NativeMethods.MESSAGE_PROPID_ADMIN_QUEUE);
                                properties.Ghost(NativeMethods.MESSAGE_PROPID_ADMIN_QUEUE_LEN);
                            }
                        }
                        //Encryption algorithm cannot be set if not using Encryption
                        if (filter.EncryptionAlgorithm)
                        {
                            if ((filter.UseEncryption && !UseEncryption) || !filter.UseEncryption)
                            {
                                properties.Ghost(NativeMethods.MESSAGE_PROPID_ENCRYPTION_ALG);
                            }
                        }
                        if (filter.DigitalSignature)
                        {
                            if (properties.GetUI4(NativeMethods.MESSAGE_PROPID_SIGNATURE_LEN) == 0)
                            {
                                properties.Ghost(NativeMethods.MESSAGE_PROPID_SIGNATURE);
                                properties.Ghost(NativeMethods.MESSAGE_PROPID_SIGNATURE_LEN);
                            }
                        }
                        if (filter.DestinationSymmetricKey)
                        {
                            if (properties.GetUI4(NativeMethods.MESSAGE_PROPID_DEST_SYMM_KEY_LEN) == 0)
                            {
                                properties.Ghost(NativeMethods.MESSAGE_PROPID_DEST_SYMM_KEY);
                                properties.Ghost(NativeMethods.MESSAGE_PROPID_DEST_SYMM_KEY_LEN);
                            }
                        }
                        if (filter.ResponseQueue)
                        {
                            if (properties.GetUI4(NativeMethods.MESSAGE_PROPID_RESP_QUEUE_LEN) == 0)
                            {
                                properties.Ghost(NativeMethods.MESSAGE_PROPID_RESP_QUEUE);
                                properties.Ghost(NativeMethods.MESSAGE_PROPID_RESP_QUEUE_LEN);
                            }
                        }
                        if (filter.TransactionStatusQueue)
                        {
                            if (properties.GetUI4(NativeMethods.MESSAGE_PROPID_XACT_STATUS_QUEUE_LEN) == 0)
                            {
                                properties.Ghost(NativeMethods.MESSAGE_PROPID_XACT_STATUS_QUEUE);
                                properties.Ghost(NativeMethods.MESSAGE_PROPID_XACT_STATUS_QUEUE_LEN);
                            }
                        }

                        receiveCreated = false;
                    }
                }
            }
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.IdFromByteArray"]/*' />
        /// <internalonly/>
        static string IdFromByteArray(byte[] bytes)
        {
            StringBuilder result = new();
            byte[] guidBytes = new byte[GenericIdSize];
            Array.Copy(bytes, guidBytes, GenericIdSize);
            int id = BitConverter.ToInt32(bytes, GenericIdSize);
            result.Append(new Guid(guidBytes).ToString());
            result.Append('\\');
            result.Append(id);
            return result.ToString();
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.IdToByteArray"]/*' />
        /// <internalonly/>
        byte[] IdToByteArray(string id)
        {
            string[] pieces = id.Split(['\\']);
            if (pieces.Length != 2)
            {
                throw new InvalidOperationException(Res.GetString(Res.InvalidId));
            }

            Guid guid;
            try
            {
                guid = new Guid(pieces[0]);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException(Res.GetString(Res.InvalidId));
            }

            int integerId;
            try
            {
                integerId = Convert.ToInt32(pieces[1], CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException(Res.GetString(Res.InvalidId));
            }
            catch (OverflowException)
            {
                throw new InvalidOperationException(Res.GetString(Res.InvalidId));
            }

            byte[] bytes = new byte[MessageIdSize];
            Array.Copy(guid.ToByteArray(), bytes, GenericIdSize);
            Array.Copy(BitConverter.GetBytes(integerId), 0, bytes, GenericIdSize, 4);
            return bytes;
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.Lock"]/*' />
        /// <internalonly/>
        internal MessagePropertyVariants.MQPROPS Lock()
        {
            return properties.Lock();
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.StringFromBytes"]/*' />
        /// <internalonly/>
        internal static string StringFromBytes(byte[] bytes, int len)
        {
            //If the string ends with 0, lets trim it.
            if (len != 0 && bytes[(len * 2) - 1] == 0 && bytes[(len * 2) - 2] == 0)
            {
                --len;
            }

            char[] charBuffer = new char[len];
            Encoding.Unicode.GetChars(bytes, 0, len * 2, charBuffer, 0);
            return new string(charBuffer, 0, len);
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.StringToBytes"]/*' />
        /// <internalonly/>
        internal static byte[] StringToBytes(string value)
        {
            int size = (value.Length * 2) + 1;
            byte[] byteBuffer = new byte[size];
            byteBuffer[size - 1] = 0;
            Encoding.Unicode.GetBytes(value.ToCharArray(), 0, value.Length, byteBuffer, 0);
            return byteBuffer;
        }

        /// <include file='doc\Message.uex' path='docs/doc[@for="Message.Unlock"]/*' />
        /// <internalonly/>
        internal void Unlock()
        {
            properties.Unlock();
        }
    }
}
