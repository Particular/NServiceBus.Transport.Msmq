//------------------------------------------------------------------------------
// <copyright file="MessageQueue.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Messaging.Msmq
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.ComponentModel.Design;
    using System.Diagnostics;
    using System.Globalization;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;
    using Messaging.Msmq.Interop;

    /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue"]/*' />
    /// <devdoc>
    ///    <para>
    ///       Provides
    ///       access to a Message Queuing backend queue resource.
    ///    </para>
    /// </devdoc>
    public class MessageQueue : Component, IEnumerable
    {
        //Public constants
        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.InfiniteTimeout"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Specifies that
        ///       there is no
        ///       timeout period for calls to peek or receive messages.
        ///    </para>
        /// </devdoc>
        public static readonly TimeSpan InfiniteTimeout = TimeSpan.FromMilliseconds(uint.MaxValue);
        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.InfiniteQueueSize"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public static readonly long InfiniteQueueSize = uint.MaxValue;


        //Internal members


        DefaultPropertiesToSend defaultProperties;
        MessagePropertyFilter receiveFilter;
        int sharedMode;
        string formatName;
        string queuePath;
        string path;
        readonly bool enableCache;
        QueuePropertyVariants properties;
        IMessageFormatter formatter;

        // Double-checked locking pattern requires volatile for read/write synchronization
        static volatile string computerName;

        internal static readonly Version OSVersion = Environment.OSVersion.Version;
        internal static readonly Version WinXP = new(5, 1);
        internal static readonly bool Msmq3OrNewer = OSVersion >= WinXP;

        //Cached properties
        QueuePropertyFilter filter;
        bool authenticate;
        short basePriority;
        DateTime createTime;
        int encryptionLevel;
        Guid id;
        string label;
        string multicastAddress;
        DateTime lastModifyTime;
        long journalSize;
        long queueSize;
        Guid queueType;
        bool useJournaling;
        MQCacheableInfo mqInfo;

        // Double-checked locking pattern requires volatile for read/write synchronization
        //Async IO support
        volatile bool attached;
        bool useThreadPool;
        AsyncCallback onRequestCompleted;
        PeekCompletedEventHandler onPeekCompleted;
        ReceiveCompletedEventHandler onReceiveCompleted;
        ISynchronizeInvoke synchronizingObject;

        // Double-checked locking pattern requires volatile for read/write synchronization
        volatile Hashtable outstandingAsyncRequests;

        //Path sufixes
        static readonly string SUFIX_PRIVATE = "\\PRIVATE$";
        static readonly string SUFIX_JOURNAL = "\\JOURNAL$";
        static readonly string SUFIX_DEADLETTER = "\\DEADLETTER$";
        static readonly string SUFIX_DEADXACT = "\\XACTDEADLETTER$";

        //Path prefixes
        static readonly string PREFIX_LABEL = "LABEL:";
        static readonly string PREFIX_FORMAT_NAME = "FORMATNAME:";

        //Connection pooling support
        static readonly CacheTable<string, string> formatNameCache =
            new("formatNameCache", 4, new TimeSpan(0, 0, 100));   // path -> formatname

        static readonly CacheTable<QueueInfoKeyHolder, MQCacheableInfo> queueInfoCache =
            new("queue info", 4, new TimeSpan(0, 0, 100));        // <formatname, accessMode> -> <readHandle. writeHandle, isTrans>

        // Double-checked locking pattern requires volatile for read/write synchronization
        volatile QueueInfoKeyHolder queueInfoKey = null;

        //Code Acess Security support
        bool administerGranted;
        bool browseGranted;
        bool sendGranted;
        bool receiveGranted;
        bool peekGranted;

        readonly object syncRoot = new();
        static readonly object staticSyncRoot = new();

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.MessageQueue"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Initializes a new instance of the <see cref='System.Messaging.MessageQueue'/> class. To use the object instantiated by the default
        ///       constructor, the <see cref='System.Messaging.MessageQueue.Path'/>
        ///       property must be set.
        ///    </para>
        /// </devdoc>
        //
        public MessageQueue()
        {
            path = string.Empty;
            AccessMode = QueueAccessMode.SendAndReceive;
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.MessageQueue1"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Initializes a new instance of the <see cref='System.Messaging.MessageQueue'/>
        ///       class that references the Message Queuing application resource specified by the
        ///    <paramref name="path"/>
        ///    parameter.
        /// </para>
        /// </devdoc>
        public MessageQueue(string path)
            : this(path, false, EnableConnectionCache)
        {
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.MessageQueue5"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Initializes a new instance of the <see cref='System.Messaging.MessageQueue'/>
        ///       class that references the Message Queuing application resource specified by the
        ///    <paramref name="path"/> parameter and having the specifed access mode.
        /// </para>
        /// </devdoc>
        public MessageQueue(string path, QueueAccessMode accessMode)
            : this(path, false, EnableConnectionCache, accessMode)
        {
        }



        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.MessageQueue2"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Initializes a new instance of the <see cref='System.Messaging.MessageQueue'/> class that references the
        ///       Message Queuing application resource specified by the <paramref name="path"/> parameter,
        ///       and has the specified queue read access restriction.
        ///    </para>
        /// </devdoc>
        public MessageQueue(string path, bool sharedModeDenyReceive)
            : this(path, sharedModeDenyReceive, EnableConnectionCache)
        {
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.MessageQueue3"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Initializes a new instance of the <see cref='System.Messaging.MessageQueue'/> class that references the
        ///       Message Queuing application resource specified by the <paramref name="path"/> parameter,
        ///       has the specified queue read access restriction and whether to cache handles
        ///    </para>
        /// </devdoc>
        public MessageQueue(string path, bool sharedModeDenyReceive, bool enableCache)
        {
            this.path = path;
            this.enableCache = enableCache;
            if (sharedModeDenyReceive)
            {
                sharedMode = NativeMethods.QUEUE_SHARED_MODE_DENY_RECEIVE;
            }
            AccessMode = QueueAccessMode.SendAndReceive;
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.MessageQueue4"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Initializes a new instance of the <see cref='System.Messaging.MessageQueue'/> class that references the
        ///       Message Queuing application resource specified by the <paramref name="path"/> parameter,
        ///       has the specified queue read access restriction, whether to cache handles,
        ///       and specified access mode.
        ///    </para>
        /// </devdoc>
        public MessageQueue(string path, bool sharedModeDenyReceive,
                            bool enableCache, QueueAccessMode accessMode)
        {
            this.path = path;
            this.enableCache = enableCache;
            if (sharedModeDenyReceive)
            {
                sharedMode = NativeMethods.QUEUE_SHARED_MODE_DENY_RECEIVE;
            }
            SetAccessMode(accessMode);
        }





        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.MessageQueue3"]/*' />
        /// <internalonly/>
        internal MessageQueue(string path, Guid id)
        {
            PropertyFilter.Id = true;
            this.id = id;
            this.path = path;
            AccessMode = QueueAccessMode.SendAndReceive;

        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.AccessMode"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets value specifying access mode of the queue
        ///    </para>
        /// </devdoc>
        public QueueAccessMode AccessMode { get; private set; }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Authenticate"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets a value specifying whether the queue only accepts authenticated
        ///       messages.
        ///    </para>
        /// </devdoc>
        public bool Authenticate
        {
            get
            {
                if (!PropertyFilter.Authenticate)
                {
                    Properties.SetUI1(NativeMethods.QUEUE_PROPID_AUTHENTICATE, 0);
                    GenerateQueueProperties();
                    authenticate = Properties.GetUI1(NativeMethods.QUEUE_PROPID_AUTHENTICATE) != NativeMethods.QUEUE_AUTHENTICATE_NONE;
                    PropertyFilter.Authenticate = true;
                    Properties.Remove(NativeMethods.QUEUE_PROPID_AUTHENTICATE);
                }

                return authenticate;
            }

            set
            {
                if (value)
                {
                    Properties.SetUI1(NativeMethods.QUEUE_PROPID_AUTHENTICATE, NativeMethods.QUEUE_AUTHENTICATE_AUTHENTICATE);
                }
                else
                {
                    Properties.SetUI1(NativeMethods.QUEUE_PROPID_AUTHENTICATE, NativeMethods.QUEUE_AUTHENTICATE_NONE);
                }

                SaveQueueProperties();
                authenticate = value;
                PropertyFilter.Authenticate = true;
                Properties.Remove(NativeMethods.QUEUE_PROPID_AUTHENTICATE);
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.BasePriority"]/*' />
        /// <devdoc>
        ///    <para>Gets or sets a value indicating the base
        ///       priority used to route a public queue's messages over the network.</para>
        /// </devdoc>
        public short BasePriority
        {
            get
            {
                if (!PropertyFilter.BasePriority)
                {
                    Properties.SetI2(NativeMethods.QUEUE_PROPID_BASEPRIORITY, 0);
                    GenerateQueueProperties();
                    basePriority = properties.GetI2(NativeMethods.QUEUE_PROPID_BASEPRIORITY);
                    PropertyFilter.BasePriority = true;
                    Properties.Remove(NativeMethods.QUEUE_PROPID_BASEPRIORITY);
                }

                return basePriority;

            }

            set
            {
                Properties.SetI2(NativeMethods.QUEUE_PROPID_BASEPRIORITY, value);
                SaveQueueProperties();
                basePriority = value;
                PropertyFilter.BasePriority = true;
                Properties.Remove(NativeMethods.QUEUE_PROPID_BASEPRIORITY);
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.CanRead"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets a value indicating whether the <see cref='System.Messaging.MessageQueue'/>
        ///       has read permission.
        ///    </para>
        /// </devdoc>
        public bool CanRead
        {
            get
            {
                if (!browseGranted)
                {
                    browseGranted = true;
                }

                return MQInfo.CanRead;
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.CanWrite"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets a value indicating whether the <see cref='System.Messaging.MessageQueue'/>
        ///       has write permission.
        ///    </para>
        /// </devdoc>
        public bool CanWrite
        {
            get
            {
                if (!browseGranted)
                {
                    browseGranted = true;
                }

                return MQInfo.CanWrite;
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Category"]/*' />
        /// <devdoc>
        ///    <para>Gets or sets the queue type.</para>
        /// </devdoc>
        public Guid Category
        {
            get
            {
                if (!PropertyFilter.Category)
                {
                    Properties.SetNull(NativeMethods.QUEUE_PROPID_TYPE);
                    GenerateQueueProperties();
                    byte[] bytes = new byte[16];
                    IntPtr handle = Properties.GetIntPtr(NativeMethods.QUEUE_PROPID_TYPE);
                    if (handle != IntPtr.Zero)
                    {
                        Marshal.Copy(handle, bytes, 0, 16);
                        //MSMQ allocated memory for this operation, needs to be freed
                        SafeNativeMethods.MQFreeMemory(handle);
                    }

                    queueType = new Guid(bytes);
                    PropertyFilter.Category = true;
                    Properties.Remove(NativeMethods.QUEUE_PROPID_TYPE);
                }
                return queueType;
            }

            set
            {
                Properties.SetGuid(NativeMethods.QUEUE_PROPID_TYPE, value.ToByteArray());
                SaveQueueProperties();
                queueType = value;
                PropertyFilter.Category = true;
                Properties.Remove(NativeMethods.QUEUE_PROPID_TYPE);
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ComputerName"]/*' />
        /// <internalonly/>
        internal static string ComputerName
        {
            get
            {
                if (computerName == null)
                {
                    lock (staticSyncRoot)
                    {
                        if (computerName == null)
                        {
                            StringBuilder sb = new(256);
                            SafeNativeMethods.GetComputerName(sb, [sb.Capacity]);
                            computerName = sb.ToString();
                        }
                    }
                }

                return computerName;
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.CreateTime"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets the time and date of the queue's creation.
        ///    </para>
        /// </devdoc>
        public DateTime CreateTime
        {
            get
            {
                if (!PropertyFilter.CreateTime)
                {
                    DateTime time = new(1970, 1, 1);
                    Properties.SetI4(NativeMethods.QUEUE_PROPID_CREATE_TIME, 0);
                    GenerateQueueProperties();
                    createTime = time.AddSeconds(properties.GetI4(NativeMethods.QUEUE_PROPID_CREATE_TIME)).ToLocalTime();
                    PropertyFilter.CreateTime = true;
                    Properties.Remove(NativeMethods.QUEUE_PROPID_CREATE_TIME);
                }

                return createTime;
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.DefaultPropertiesToSend"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets the properties to be used by
        ///       default when sending messages to the queue referenced by this <see cref='System.Messaging.MessageQueue'/>
        ///       .
        ///    </para>
        /// </devdoc>
        public DefaultPropertiesToSend DefaultPropertiesToSend
        {
            get
            {
                if (defaultProperties == null)
                {
                    if (DesignMode)
                    {
                        defaultProperties = new DefaultPropertiesToSend(true);
                    }
                    else
                    {
                        defaultProperties = new DefaultPropertiesToSend();
                    }
                }

                return defaultProperties;
            }

            set
            {
                defaultProperties = value;
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.DenySharedReceive"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Specifies the shared mode for the queue that this object
        ///       references. If <see langword='true'/> ,
        ///       no other queue object will be able to receive messages from the queue resource.
        ///    </para>
        /// </devdoc>
        public bool DenySharedReceive
        {
            get
            {
                return sharedMode == NativeMethods.QUEUE_SHARED_MODE_DENY_RECEIVE;
            }
            set
            {
                if (value && (sharedMode != NativeMethods.QUEUE_SHARED_MODE_DENY_RECEIVE))
                {
                    Close();
                    sharedMode = NativeMethods.QUEUE_SHARED_MODE_DENY_RECEIVE;
                }
                else if (!value && (sharedMode == NativeMethods.QUEUE_SHARED_MODE_DENY_RECEIVE))
                {
                    Close();
                    sharedMode = NativeMethods.QUEUE_SHARED_MODE_DENY_NONE;
                }
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.EnableConnectionCache"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        [Browsable(false)]
        public static bool EnableConnectionCache { get; set; } = false;

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.EncryptionRequired"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets a value indicating whether the queue only accepts non-private
        ///       (non-encrypted) messages.
        ///    </para>
        /// </devdoc>
        public EncryptionRequired EncryptionRequired
        {
            get
            {
                if (!PropertyFilter.EncryptionLevel)
                {
                    Properties.SetUI4(NativeMethods.QUEUE_PROPID_PRIV_LEVEL, 0);
                    GenerateQueueProperties();
                    encryptionLevel = Properties.GetUI4(NativeMethods.QUEUE_PROPID_PRIV_LEVEL);
                    PropertyFilter.EncryptionLevel = true;
                    Properties.Remove(NativeMethods.QUEUE_PROPID_PRIV_LEVEL);
                }
                return (EncryptionRequired)encryptionLevel;
            }

            set
            {
                if (!ValidationUtility.ValidateEncryptionRequired(value))
                {
                    throw new InvalidEnumArgumentException("value", (int)value, typeof(EncryptionRequired));
                }

                Properties.SetUI4(NativeMethods.QUEUE_PROPID_PRIV_LEVEL, (int)value);
                SaveQueueProperties();
                encryptionLevel = properties.GetUI4(NativeMethods.QUEUE_PROPID_PRIV_LEVEL);
                PropertyFilter.EncryptionLevel = true;
                Properties.Remove(NativeMethods.QUEUE_PROPID_PRIV_LEVEL);
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.FormatName"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets the unique name that was generated for the queue when the queue was created.
        ///    </para>
        /// </devdoc>
        public string FormatName
        {
            get
            {
                if (formatName == null)
                {
                    if (path == null || path.Length == 0)
                    {
                        return string.Empty;
                    }

                    string pathUpper = path.ToUpper(CultureInfo.InvariantCulture);

                    // see if we already have this cached
                    if (enableCache)
                    {
                        formatName = formatNameCache.Get(pathUpper);
                    }

                    // not in the cache?  keep working.
                    if (formatName == null)
                    {
                        if (PropertyFilter.Id)
                        {
                            //Improves performance when enumerating queues.
                            //This codepath will only be executed when accessing
                            //a queue returned by MessageQueueEnumerator.
                            int result;
                            StringBuilder newFormatName = new(NativeMethods.MAX_LABEL_LEN);
                            result = NativeMethods.MAX_LABEL_LEN;
                            int status = SafeNativeMethods.MQInstanceToFormatName(id.ToByteArray(), newFormatName, ref result);
                            if (status != 0)
                            {
                                throw new MessageQueueException(status);
                            }

                            formatName = newFormatName.ToString();
                            return formatName;
                        }

                        if (pathUpper.StartsWith(PREFIX_FORMAT_NAME))
                        {
                            formatName = path[PREFIX_FORMAT_NAME.Length..];
                        }
                        else if (pathUpper.StartsWith(PREFIX_LABEL))
                        {
                            MessageQueue labeledQueue = ResolveQueueFromLabel(path, true);
                            formatName = labeledQueue.FormatName;
                            queuePath = labeledQueue.QueuePath;
                        }
                        else
                        {
                            queuePath = path;
                            formatName = ResolveFormatNameFromQueuePath(queuePath, true);
                        }

                        formatNameCache.Put(pathUpper, formatName);
                    }
                }

                return formatName;
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Formatter"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or
        ///       sets a
        ///       formatter class used to serialize messages read or written to
        ///       the message body.
        ///    </para>
        /// </devdoc>
        public IMessageFormatter Formatter
        {
            get
            {
                if (formatter == null && !DesignMode)
                {
                    formatter = new XmlMessageFormatter();
                }

                return formatter;
            }

            set
            {
                formatter = value;
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Id"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets the Message Queuing unique identifier for the queue.
        ///    </para>
        /// </devdoc>
        public Guid Id
        {
            get
            {
                if (!PropertyFilter.Id)
                {
                    Properties.SetNull(NativeMethods.QUEUE_PROPID_INSTANCE);
                    GenerateQueueProperties();
                    byte[] bytes = new byte[16];
                    IntPtr handle = Properties.GetIntPtr(NativeMethods.QUEUE_PROPID_INSTANCE);
                    if (handle != IntPtr.Zero)
                    {
                        Marshal.Copy(handle, bytes, 0, 16);
                        //MSMQ allocated memory for this operation, needs to be freed
                        SafeNativeMethods.MQFreeMemory(handle);
                    }
                    id = new Guid(bytes);
                    PropertyFilter.Id = true;
                    Properties.Remove(NativeMethods.QUEUE_PROPID_INSTANCE);
                }
                return id;
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Label"]/*' />
        /// <devdoc>
        ///    <para>Gets or sets the queue description.</para>
        /// </devdoc>
        public string Label
        {
            get
            {
                if (!PropertyFilter.Label)
                {
                    Properties.SetNull(NativeMethods.QUEUE_PROPID_LABEL);
                    GenerateQueueProperties();
                    string description = null;
                    IntPtr handle = Properties.GetIntPtr(NativeMethods.QUEUE_PROPID_LABEL);
                    if (handle != IntPtr.Zero)
                    {
                        //Using Unicode API even on Win9x
                        description = Marshal.PtrToStringUni(handle);
                        //MSMQ allocated memory for this operation, needs to be freed
                        SafeNativeMethods.MQFreeMemory(handle);
                    }

                    label = description;
                    PropertyFilter.Label = true;
                    Properties.Remove(NativeMethods.QUEUE_PROPID_LABEL);
                }

                return label;
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);

                //Borrow this function from message
                Properties.SetString(NativeMethods.QUEUE_PROPID_LABEL, Message.StringToBytes(value));
                SaveQueueProperties();
                label = value;
                PropertyFilter.Label = true;
                Properties.Remove(NativeMethods.QUEUE_PROPID_LABEL);
            }
        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.LastModifyTime"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Indicates the last time the properties of a queue were modified.
        ///    </para>
        /// </devdoc>
        public DateTime LastModifyTime
        {
            get
            {
                if (!PropertyFilter.LastModifyTime)
                {
                    DateTime time = new(1970, 1, 1);
                    Properties.SetI4(NativeMethods.QUEUE_PROPID_MODIFY_TIME, 0);
                    GenerateQueueProperties();
                    lastModifyTime = time.AddSeconds(properties.GetI4(NativeMethods.QUEUE_PROPID_MODIFY_TIME)).ToLocalTime();
                    PropertyFilter.LastModifyTime = true;
                    Properties.Remove(NativeMethods.QUEUE_PROPID_MODIFY_TIME);
                }

                return lastModifyTime;
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.MachineName"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets the name of the computer where
        ///       the queue referenced by this <see cref='System.Messaging.MessageQueue'/>
        ///       is located.
        ///    </para>
        /// </devdoc>
        public string MachineName
        {
            get
            {
                string queuePath = QueuePath;
                if (queuePath.Length == 0)
                {
                    return queuePath;
                }
                return queuePath[..queuePath.IndexOf('\\')];
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);

                if (!SyntaxCheck.CheckMachineName(value))
                {
                    throw new ArgumentException(Res.GetString(Res.InvalidProperty, "MachineName", value));
                }

                StringBuilder newPath = new();
                if ((path == null || path.Length == 0) && formatName == null)
                {
                    //Need to default to an existing queue, for instance Journal.
                    newPath.Append(value);
                    newPath.Append(SUFIX_JOURNAL);
                }
                else
                {
                    newPath.Append(value);
                    newPath.Append('\\');
                    newPath.Append(QueueName);
                }
                Path = newPath.ToString();
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.MaximumJournalSize"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets the maximum size of the journal queue.
        ///    </para>
        /// </devdoc>
        public long MaximumJournalSize
        {
            get
            {
                if (!PropertyFilter.MaximumJournalSize)
                {
                    Properties.SetUI4(NativeMethods.QUEUE_PROPID_JOURNAL_QUOTA, 0);
                    GenerateQueueProperties();
                    journalSize = (uint)properties.GetUI4(NativeMethods.QUEUE_PROPID_JOURNAL_QUOTA);
                    PropertyFilter.MaximumJournalSize = true;
                    Properties.Remove(NativeMethods.QUEUE_PROPID_JOURNAL_QUOTA);
                }

                return journalSize;
            }

            set
            {
                if (value > InfiniteQueueSize || value < 0)
                {
                    throw new ArgumentException(Res.GetString(Res.InvalidProperty, "MaximumJournalSize", value));
                }

                Properties.SetUI4(NativeMethods.QUEUE_PROPID_JOURNAL_QUOTA, (int)(uint)value);
                SaveQueueProperties();
                journalSize = value;
                PropertyFilter.MaximumJournalSize = true;
                Properties.Remove(NativeMethods.QUEUE_PROPID_JOURNAL_QUOTA);
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.MaximumQueueSize"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets the maximum size of the queue.
        ///    </para>
        /// </devdoc>
        public long MaximumQueueSize
        {
            get
            {
                if (!PropertyFilter.MaximumQueueSize)
                {
                    Properties.SetUI4(NativeMethods.QUEUE_PROPID_QUOTA, 0);
                    GenerateQueueProperties();
                    queueSize = (uint)properties.GetUI4(NativeMethods.QUEUE_PROPID_QUOTA);
                    PropertyFilter.MaximumQueueSize = true;
                    Properties.Remove(NativeMethods.QUEUE_PROPID_QUOTA);
                }

                return queueSize;
            }

            set
            {
                if (value > InfiniteQueueSize || value < 0)
                {
                    throw new ArgumentException(Res.GetString(Res.InvalidProperty, "MaximumQueueSize", value));
                }

                Properties.SetUI4(NativeMethods.QUEUE_PROPID_QUOTA, (int)(uint)value);
                SaveQueueProperties();
                queueSize = value;
                PropertyFilter.MaximumQueueSize = true;
                Properties.Remove(NativeMethods.QUEUE_PROPID_QUOTA);
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.MessageReadPropertyFilter"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets the property filter for
        ///       receiving messages.
        ///    </para>
        /// </devdoc>
        public MessagePropertyFilter MessageReadPropertyFilter
        {
            get
            {
                if (receiveFilter == null)
                {
                    receiveFilter = new MessagePropertyFilter();
                    receiveFilter.SetDefaults();
                }

                return receiveFilter;
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);

                receiveFilter = value;
            }
        }

        internal MQCacheableInfo MQInfo
        {
            get
            {
                if (mqInfo == null)
                {
                    MQCacheableInfo cachedInfo = queueInfoCache.Get(QueueInfoKey);
                    if (sharedMode == NativeMethods.QUEUE_SHARED_MODE_DENY_RECEIVE || !enableCache)
                    {
                        cachedInfo?.CloseIfNotReferenced();

                        // don't use the cache
                        mqInfo = new MQCacheableInfo(FormatName, AccessMode, sharedMode);
                        mqInfo.AddRef();
                    }
                    else
                    {
                        // use the cache
                        if (cachedInfo != null)
                        {
                            cachedInfo.AddRef();
                            mqInfo = cachedInfo;
                        }
                        else
                        {
                            mqInfo = new MQCacheableInfo(FormatName, AccessMode, sharedMode);
                            mqInfo.AddRef();
                            queueInfoCache.Put(QueueInfoKey, mqInfo);
                        }
                    }
                }

                return mqInfo;
            }
        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.MulticastAddress"]/*' />
        /// <devdoc>
        ///    <para>Gets or sets the IP multicast address associated with the queue.</para>
        /// </devdoc>
        public string MulticastAddress
        {
            get
            {
                if (!Msmq3OrNewer)
                { //this feature is unavailable on win2k
                    // don't throw in design mode: this makes component unusable
                    if (DesignMode)
                    {
                        return string.Empty;
                    }
                    else
                    {
                        throw new PlatformNotSupportedException(Res.GetString(Res.PlatformNotSupported));
                    }
                }

                if (!PropertyFilter.MulticastAddress)
                {
                    Properties.SetNull(NativeMethods.QUEUE_PROPID_MULTICAST_ADDRESS);
                    GenerateQueueProperties();
                    string address = null;
                    IntPtr handle = Properties.GetIntPtr(NativeMethods.QUEUE_PROPID_MULTICAST_ADDRESS);
                    if (handle != IntPtr.Zero)
                    {
                        address = Marshal.PtrToStringUni(handle);
                        //MSMQ allocated memory for this operation, needs to be freed
                        SafeNativeMethods.MQFreeMemory(handle);
                    }

                    multicastAddress = address;
                    PropertyFilter.MulticastAddress = true;
                    Properties.Remove(NativeMethods.QUEUE_PROPID_MULTICAST_ADDRESS);
                }

                return multicastAddress;
            }
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                if (!Msmq3OrNewer) //this feature is unavailable on win2k
                {
                    throw new PlatformNotSupportedException(Res.GetString(Res.PlatformNotSupported));
                }

                if (value.Length == 0) // used to disassocciate queue from a muliticast address
                {
                    Properties.SetEmpty(NativeMethods.QUEUE_PROPID_MULTICAST_ADDRESS);
                }
                else //Borrow this function from message
                {
                    Properties.SetString(NativeMethods.QUEUE_PROPID_MULTICAST_ADDRESS, Message.StringToBytes(value));
                }

                SaveQueueProperties();
                multicastAddress = value;
                PropertyFilter.MulticastAddress = true;
                Properties.Remove(NativeMethods.QUEUE_PROPID_MULTICAST_ADDRESS);
            }
        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Path"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets the queue's path. When setting the <see cref='System.Messaging.MessageQueue.Path'/>, this points the <see cref='System.Messaging.MessageQueue'/>
        ///       to a new queue.
        ///    </para>
        /// </devdoc>
        public string Path
        {
            get
            {
                return path;
            }

            set
            {
                value ??= string.Empty;

                if (!ValidatePath(value, false))
                {
                    throw new ArgumentException(Res.GetString(Res.PathSyntax));
                }

                if (!string.IsNullOrEmpty(path))
                {
                    Close();
                }

                path = value;
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Properties"]/*' />
        /// <internalonly/>
        QueuePropertyVariants Properties
        {
            get
            {
                properties ??= new QueuePropertyVariants();

                return properties;
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.PropertyFilter"]/*' />
        /// <internalonly/>
        QueuePropertyFilter PropertyFilter
        {
            get
            {
                filter ??= new QueuePropertyFilter();

                return filter;
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.QueueName"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets the friendly
        ///       name that identifies the queue.
        ///    </para>
        /// </devdoc>
        public string QueueName
        {
            get
            {
                string queuePath = QueuePath;
                if (queuePath.Length == 0)
                {
                    return queuePath;
                }
                return queuePath[(queuePath.IndexOf('\\') + 1)..];
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);

                StringBuilder newPath = new();
                if ((path == null || path.Length == 0) && formatName == null)
                {
                    newPath.Append(".\\");
                    newPath.Append(value);
                }
                else
                {
                    newPath.Append(MachineName);
                    newPath.Append('\\');
                    newPath.Append(value);
                }
                Path = newPath.ToString();
            }
        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.QueuePath"]/*' />
        /// <internalonly/>
        internal string QueuePath
        {
            get
            {
                if (queuePath == null)
                {
                    if (path == null || path.Length == 0)
                    {
                        return string.Empty;
                    }

                    string pathUpper = path.ToUpper(CultureInfo.InvariantCulture);
                    if (pathUpper.StartsWith(PREFIX_LABEL))
                    {
                        MessageQueue labeledQueue = ResolveQueueFromLabel(path, true);
                        formatName = labeledQueue.FormatName;
                        queuePath = labeledQueue.QueuePath;
                    }
                    else if (pathUpper.StartsWith(PREFIX_FORMAT_NAME))
                    {
                        Properties.SetNull(NativeMethods.QUEUE_PROPID_PATHNAME);
                        GenerateQueueProperties();
                        string description = null;
                        IntPtr handle = Properties.GetIntPtr(NativeMethods.QUEUE_PROPID_PATHNAME);
                        if (handle != IntPtr.Zero)
                        {
                            //Using Unicode API even on Win9x
                            description = Marshal.PtrToStringUni(handle);
                            //MSMQ allocated memory for this operation, needs to be freed
                            SafeNativeMethods.MQFreeMemory(handle);
                        }
                        Properties.Remove(NativeMethods.QUEUE_PROPID_PATHNAME);
                        queuePath = description;
                    }
                    else
                    {
                        queuePath = path;
                    }
                }
                return queuePath;
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ReadHandle"]/*' />
        /// <devdoc>
        ///    <para>
        ///       The native handle used to receive messages from the message queue
        ///    </para>
        /// </devdoc>
        public IntPtr ReadHandle
        {
            get
            {
                if (!receiveGranted)
                {
                    receiveGranted = true;
                }

                return MQInfo.ReadHandle.DangerousGetHandle();
            }
        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.SynchronizingObject"]/*' />
        /// <devdoc>
        ///   Represents the object used to marshal the event handler
        ///   calls issued as a result of a BeginReceive or BeginPeek
        ///  request into a specific thread. Normally this property will
        ///  be set when the component is placed inside a control or
        ///  a from, since those components are bound to a specific
        ///  thread.
        /// </devdoc>
        public ISynchronizeInvoke SynchronizingObject
        {
            get
            {
                if (synchronizingObject == null && DesignMode)
                {
                    var host = (IDesignerHost)GetService(typeof(IDesignerHost));
                    if (host != null)
                    {
                        object baseComponent = host.RootComponent;
                        if (baseComponent is not null and ISynchronizeInvoke)
                        {
                            synchronizingObject = (ISynchronizeInvoke)baseComponent;
                        }
                    }
                }

                return synchronizingObject;
            }

            set
            {
                synchronizingObject = value;
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Transactional"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets
        ///       a value indicating whether the queue supports transactions.
        ///    </para>
        /// </devdoc>
        public bool Transactional
        {
            get
            {
                if (!browseGranted)
                {
                    browseGranted = true;
                }

                return MQInfo.Transactional;
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.UseJournalQueue"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Gets or sets a value indicating whether retrieved messages are copied to the
        ///       journal queue.
        ///    </para>
        /// </devdoc>
        public bool UseJournalQueue
        {
            get
            {
                if (!PropertyFilter.UseJournalQueue)
                {
                    Properties.SetUI1(NativeMethods.QUEUE_PROPID_JOURNAL, 0);
                    GenerateQueueProperties();
                    useJournaling = Properties.GetUI1(NativeMethods.QUEUE_PROPID_JOURNAL) != NativeMethods.QUEUE_JOURNAL_NONE;
                    PropertyFilter.UseJournalQueue = true;
                    Properties.Remove(NativeMethods.QUEUE_PROPID_JOURNAL);
                }
                return useJournaling;
            }

            set
            {
                if (value)
                {
                    Properties.SetUI1(NativeMethods.QUEUE_PROPID_JOURNAL, NativeMethods.QUEUE_JOURNAL_JOURNAL);
                }
                else
                {
                    Properties.SetUI1(NativeMethods.QUEUE_PROPID_JOURNAL, NativeMethods.QUEUE_JOURNAL_NONE);
                }

                SaveQueueProperties();
                useJournaling = value;
                PropertyFilter.UseJournalQueue = true;
                Properties.Remove(NativeMethods.QUEUE_PROPID_JOURNAL);
            }
        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.WriteHandle"]/*' />
        /// <devdoc>
        ///    <para>
        ///       The native handle used to send messages to the message queue
        ///    </para>
        /// </devdoc>
        public IntPtr WriteHandle
        {
            get
            {
                if (!sendGranted)
                {
                    sendGranted = true;
                }

                return MQInfo.WriteHandle.DangerousGetHandle();
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.PeekCompleted"]/*' />
        /// <devdoc>
        ///    <para>Occurs when a message is read without being removed
        ///       from the queue. This is a result of the asynchronous operation, <see cref='System.Messaging.MessageQueue.BeginPeek'/>
        ///       .</para>
        /// </devdoc>
        public event PeekCompletedEventHandler PeekCompleted
        {
            add
            {
                if (!peekGranted)
                {
                    peekGranted = true;
                }

                onPeekCompleted += value;
            }
            remove
            {
                onPeekCompleted -= value;
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ReceiveCompleted"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Occurs when a message has been taken out of the queue.
        ///       This is a result of the asynchronous operation <see cref='System.Messaging.MessageQueue.BeginReceive'/>
        ///       .
        ///    </para>
        /// </devdoc>
        public event ReceiveCompletedEventHandler ReceiveCompleted
        {
            add
            {
                if (!receiveGranted)
                {
                    receiveGranted = true;
                }

                onReceiveCompleted += value;
            }
            remove
            {
                onReceiveCompleted -= value;
            }
        }


        Hashtable OutstandingAsyncRequests
        {
            get
            {
                if (outstandingAsyncRequests == null)
                {
                    lock (syncRoot)
                    {
                        if (outstandingAsyncRequests == null)
                        {
                            var requests = Hashtable.Synchronized(new Hashtable());
                            Thread.MemoryBarrier();
                            outstandingAsyncRequests = requests;

                        }
                    }
                }

                return outstandingAsyncRequests;
            }
        }


        QueueInfoKeyHolder QueueInfoKey
        {
            get
            {
                if (queueInfoKey == null)
                {
                    lock (syncRoot)
                    {
                        if (queueInfoKey == null)
                        {
                            QueueInfoKeyHolder keyHolder = new(FormatName, AccessMode);
                            Thread.MemoryBarrier();
                            queueInfoKey = keyHolder;
                        }
                    }
                }

                return queueInfoKey;
            }
        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.BeginPeek"]/*' />
        /// <devdoc>
        ///    <para>Initiates an asynchronous peek operation with no timeout. The method
        ///       returns immediately, but the asynchronous operation is not completed until
        ///       the event handler is called. This occurs when a message is
        ///       available in the
        ///       queue.</para>
        /// </devdoc>
        public IAsyncResult BeginPeek()
        {
            return ReceiveAsync(InfiniteTimeout, CursorHandle.NullHandle, NativeMethods.QUEUE_ACTION_PEEK_CURRENT, null, null);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.BeginPeek1"]/*' />
        /// <devdoc>
        ///    <para> Initiates an asynchronous peek operation with the timeout specified.
        ///       The method returns immediately, but the asynchronous operation is not completed until
        ///       the event handler is called. This occurs when either a message is available in
        ///       the queue or the timeout
        ///       expires.</para>
        /// </devdoc>
        public IAsyncResult BeginPeek(TimeSpan timeout)
        {
            return ReceiveAsync(timeout, CursorHandle.NullHandle, NativeMethods.QUEUE_ACTION_PEEK_CURRENT, null, null);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.BeginPeek2"]/*' />
        /// <devdoc>
        ///    <para> Initiates an asynchronous peek operation with a state object that associates
        ///       information with the operation throughout the operation's
        ///       lifetime. The method returns immediately, but the asynchronous operation is not completed
        ///       until the event handler
        ///       is called. This occurs when either a message is available in the
        ///       queue or the timeout
        ///       expires.</para>
        /// </devdoc>
        public IAsyncResult BeginPeek(TimeSpan timeout, object stateObject)
        {
            return ReceiveAsync(timeout, CursorHandle.NullHandle, NativeMethods.QUEUE_ACTION_PEEK_CURRENT, null, stateObject);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.BeginPeek3"]/*' />
        /// <devdoc>
        ///    <para> Initiates an asynchronous peek operation that receives
        ///       notification through a callback which identifies the event handling method for the
        ///       operation. The method returns immediately, but the asynchronous operation is not completed
        ///       until the event handler is called. This occurs when either a message is available
        ///       in the queue or the timeout
        ///       expires.</para>
        /// </devdoc>
        public IAsyncResult BeginPeek(TimeSpan timeout, object stateObject, AsyncCallback callback)
        {
            return ReceiveAsync(timeout, CursorHandle.NullHandle, NativeMethods.QUEUE_ACTION_PEEK_CURRENT, callback, stateObject);
        }


        public IAsyncResult BeginPeek(TimeSpan timeout, Cursor cursor, PeekAction action, object state, AsyncCallback callback)
        {
            if (action is not PeekAction.Current and not PeekAction.Next)
            {
                throw new ArgumentOutOfRangeException(Res.GetString(Res.InvalidParameter, "action", action.ToString()));
            }

            ArgumentNullException.ThrowIfNull(cursor);

            return ReceiveAsync(timeout, cursor.Handle, (int)action, callback, state);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.BeginReceive"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Receives the first message available in the queue
        ///       referenced by the <see cref='System.Messaging.MessageQueue'/>
        ///       .
        ///    </para>
        /// </devdoc>
        public IAsyncResult BeginReceive()
        {
            return ReceiveAsync(InfiniteTimeout, CursorHandle.NullHandle, NativeMethods.QUEUE_ACTION_RECEIVE, null, null);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.BeginReceive1"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Receives the first message available in the queue
        ///       referenced by the <see cref='System.Messaging.MessageQueue'/> . Waits the specified interval for
        ///       the message to be
        ///       removed.
        ///    </para>
        /// </devdoc>
        public IAsyncResult BeginReceive(TimeSpan timeout)
        {
            return ReceiveAsync(timeout, CursorHandle.NullHandle, NativeMethods.QUEUE_ACTION_RECEIVE, null, null);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.BeginReceive2"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Receives the first message available in the queue
        ///       referenced by the <see cref='System.Messaging.MessageQueue'/> . Waits the specified interval
        ///       for a new message to be removed and uses the specified object to retrieve
        ///       the result.
        ///    </para>
        /// </devdoc>
        public IAsyncResult BeginReceive(TimeSpan timeout, object stateObject)
        {
            return ReceiveAsync(timeout, CursorHandle.NullHandle, NativeMethods.QUEUE_ACTION_RECEIVE, null, stateObject);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.BeginReceive3"]/*' />
        /// <devdoc>
        ///    <para>Receives the first message available in the queue
        ///       referenced by the <see cref='System.Messaging.MessageQueue'/> . Waits
        ///       the specified interval for a new message to be removed, uses the specified
        ///       object to retrieve the result, and receives notification through a
        ///       callback.</para>
        /// </devdoc>
        public IAsyncResult BeginReceive(TimeSpan timeout, object stateObject, AsyncCallback callback)
        {
            return ReceiveAsync(timeout, CursorHandle.NullHandle, NativeMethods.QUEUE_ACTION_RECEIVE, callback, stateObject);
        }


        public IAsyncResult BeginReceive(TimeSpan timeout, Cursor cursor, object state, AsyncCallback callback)
        {
            ArgumentNullException.ThrowIfNull(cursor);

            return ReceiveAsync(timeout, cursor.Handle, NativeMethods.QUEUE_ACTION_RECEIVE, callback, state);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ClearConnectionCache"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public static void ClearConnectionCache()
        {
            formatNameCache.ClearStale(new TimeSpan(0));
            queueInfoCache.ClearStale(new TimeSpan(0));
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Close"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Frees all resources allocated by the <see cref='System.Messaging.MessageQueue'/>
        ///       .
        ///    </para>
        /// </devdoc>
        public void Close()
        {
            Cleanup(true);
        }


        void Cleanup(bool disposing)
        {

            //This is generated from the path.
            //It needs to be cleared.
            formatName = null;
            queuePath = null;
            attached = false;
            administerGranted = false;
            browseGranted = false;
            sendGranted = false;
            receiveGranted = false;
            peekGranted = false;

            if (disposing)
            {
                if (mqInfo != null)
                {
                    mqInfo.Release();

                    //No need to check references in this case, the only object
                    //mqInfo is not cached if both conditions are satisified.
                    if (sharedMode == NativeMethods.QUEUE_SHARED_MODE_DENY_RECEIVE || !enableCache)
                    {
                        mqInfo.Dispose();
                    }

                    mqInfo = null;
                }
            }

        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Create"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Creates
        ///       a nontransactional Message Queuing backend queue resource with the
        ///       specified path.
        ///    </para>
        /// </devdoc>
        public static MessageQueue Create(string path)
        {
            return Create(path, false);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Create1"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Creates
        ///       a transactional or nontransactional Message Queuing backend queue resource with the
        ///       specified path.
        ///    </para>
        /// </devdoc>
        public static MessageQueue Create(string path, bool transactional)
        {
            ArgumentNullException.ThrowIfNull(path);

            if (path.Length == 0)
            {
                throw new ArgumentException(Res.GetString(Res.InvalidParameter, "path", path));
            }

            if (!IsCanonicalPath(path, true))
            {
                throw new ArgumentException(Res.GetString(Res.InvalidQueuePathToCreate, path));
            }

            //Create properties.
            QueuePropertyVariants properties = new();
            properties.SetString(NativeMethods.QUEUE_PROPID_PATHNAME, Message.StringToBytes(path));
            if (transactional)
            {
                properties.SetUI1(NativeMethods.QUEUE_PROPID_TRANSACTION, NativeMethods.QUEUE_TRANSACTIONAL_TRANSACTIONAL);
            }
            else
            {
                properties.SetUI1(NativeMethods.QUEUE_PROPID_TRANSACTION, NativeMethods.QUEUE_TRANSACTIONAL_NONE);
            }

            StringBuilder formatName = new(NativeMethods.MAX_LABEL_LEN);
            int formatNameLen = NativeMethods.MAX_LABEL_LEN;

            //Try to create queue.
            int status = UnsafeNativeMethods.MQCreateQueue(nint.Zero, properties.Lock(), formatName, ref formatNameLen);
            properties.Unlock();
            if (IsFatalError(status))
            {
                throw new MessageQueueException(status);
            }

            return new MessageQueue(path);
        }

        public Cursor CreateCursor()
        {
            return new Cursor(this);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.CreateMessageQueuesSnapshot1"]/*' />
        /// <internalonly/>
        static MessageQueue[] CreateMessageQueuesSnapshot(MessageQueueCriteria criteria)
        {
            ArrayList messageQueuesList = [];
            MessageQueueEnumerator messageQueues = GetMessageQueueEnumerator(criteria);
            while (messageQueues.MoveNext())
            {
                MessageQueue messageQueue = messageQueues.Current;
                messageQueuesList.Add(messageQueue);
            }

            var queues = new MessageQueue[messageQueuesList.Count];
            messageQueuesList.CopyTo(queues, 0);
            return queues;
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Delete"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Deletes a queue backend resource identified by
        ///       the given path.
        ///    </para>
        /// </devdoc>
        public static void Delete(string path)
        {
            ArgumentNullException.ThrowIfNull(path);

            if (path.Length == 0)
            {
                throw new ArgumentException(Res.GetString(Res.InvalidParameter, "path", path));
            }

            if (!ValidatePath(path, false))
            {
                throw new ArgumentException(Res.GetString(Res.PathSyntax));
            }

            MessageQueue queue = new(path);

            int status = UnsafeNativeMethods.MQDeleteQueue(queue.FormatName);
            if (IsFatalError(status))
            {
                throw new MessageQueueException(status);
            }

            queueInfoCache.Remove(queue.QueueInfoKey);
            formatNameCache.Remove(path.ToUpper(CultureInfo.InvariantCulture));
        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Dispose"]/*' />
        /// <devdoc>
        ///    <para>
        ///    </para>
        /// </devdoc>
        protected override void Dispose(bool disposing)
        {
            Cleanup(disposing);

            base.Dispose(disposing);
        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.EndPeek"]/*' />
        /// <devdoc>
        ///    <para>Completes an asynchronous peek operation associated with
        ///       the <paramref name="asyncResult"/>
        ///       parameter.</para>
        /// </devdoc>
#pragma warning disable CA1822 // Mark members as static
        public Message EndPeek(IAsyncResult asyncResult)
#pragma warning restore CA1822 // Mark members as static
        {
            return EndAsyncOperation(asyncResult);
        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.EndReceive"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Terminates a receive asynchronous operation identified
        ///       by the specified interface.
        ///    </para>
        /// </devdoc>
#pragma warning disable CA1822 // Mark members as static
        public Message EndReceive(IAsyncResult asyncResult)
#pragma warning restore CA1822 // Mark members as static
        {
            return EndAsyncOperation(asyncResult);
        }

        static Message EndAsyncOperation(IAsyncResult asyncResult)
        {
            ArgumentNullException.ThrowIfNull(asyncResult);

            if (asyncResult is not AsynchronousRequest)
            {
                throw new ArgumentException(Res.GetString(Res.AsyncResultInvalid));
            }

            var request = (AsynchronousRequest)asyncResult;

            return request.End();
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Exists"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Determines whether a queue with the specified path
        ///       exists.
        ///    </para>
        /// </devdoc>
        public static bool Exists(string path)
        {
            ArgumentNullException.ThrowIfNull(path);

            if (!ValidatePath(path, false))
            {
                throw new ArgumentException(Res.GetString(Res.PathSyntax));
            }

            string pathUpper = path.ToUpper(CultureInfo.InvariantCulture);
            if (pathUpper.StartsWith(PREFIX_FORMAT_NAME))
            {
                throw new InvalidOperationException(Res.GetString(Res.QueueExistsError));
            }
            else if (pathUpper.StartsWith(PREFIX_LABEL))
            {
                MessageQueue labeledQueue = ResolveQueueFromLabel(path, false);
                if (labeledQueue == null)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
            else
            {
                string formatName = ResolveFormatNameFromQueuePath(path, false);
                if (formatName == null)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.GenerateQueueProperties"]/*' />
        /// <internalonly/>
        void GenerateQueueProperties()
        {
            if (!browseGranted)
            {
                browseGranted = true;
            }

            int status = UnsafeNativeMethods.MQGetQueueProperties(FormatName, Properties.Lock());
            Properties.Unlock();
            if (IsFatalError(status))
            {
                throw new MessageQueueException(status);
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.GetAllMessages"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Returns all the messages available in the queue.
        ///    </para>
        /// </devdoc>
        public Message[] GetAllMessages()
        {
            ArrayList messageList = [];
            MessageEnumerator messages = GetMessageEnumerator2();
            while (messages.MoveNext())
            {
                Message message = messages.Current;
                messageList.Add(message);
            }

            var resultMessages = new Message[messageList.Count];
            messageList.CopyTo(resultMessages, 0);
            return resultMessages;
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.GetEnumerator"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        [Obsolete("This method returns a MessageEnumerator that implements RemoveCurrent family of methods incorrectly. Please use GetMessageEnumerator2 instead.")]
        public IEnumerator GetEnumerator()
        {
            return GetMessageEnumerator();
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.GetMachineId"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public static Guid GetMachineId(string machineName)
        {
            if (!SyntaxCheck.CheckMachineName(machineName))
            {
                throw new ArgumentException(Res.GetString(Res.InvalidParameter, "MachineName", machineName));
            }

            if (machineName == ".")
            {
                machineName = ComputerName;
            }

            MachinePropertyVariants machineProperties = new();
            byte[] bytes = new byte[16];
            machineProperties.SetNull(NativeMethods.MACHINE_ID);
            int status = UnsafeNativeMethods.MQGetMachineProperties(machineName, IntPtr.Zero, machineProperties.Lock());
            machineProperties.Unlock();
            IntPtr handle = machineProperties.GetIntPtr(NativeMethods.MACHINE_ID);
            if (IsFatalError(status))
            {
                if (handle != IntPtr.Zero)
                {
                    SafeNativeMethods.MQFreeMemory(handle);
                }

                throw new MessageQueueException(status);
            }

            if (handle != IntPtr.Zero)
            {
                Marshal.Copy(handle, bytes, 0, 16);
                SafeNativeMethods.MQFreeMemory(handle);
            }

            return new Guid(bytes);
        }


        /// <devdoc>
        ///  Represents security context that can be used to easily and efficiently
        ///  send messages in impersonating applications.
        /// </devdoc>
        public static SecurityContext GetSecurityContext()
        {
            // SECURITY: Note that this call is not marked with SUCS attribute (i.e., requires FullTrust)
            int status = NativeMethods.MQGetSecurityContextEx(out SecurityContextHandle handle);
            if (IsFatalError(status))
            {
                throw new MessageQueueException(status);
            }

            return new SecurityContext(handle);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.GetMessageQueueEnumerator"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Creates an enumerator object for the message queues
        ///       available on the network.
        ///    </para>
        /// </devdoc>
        public static MessageQueueEnumerator GetMessageQueueEnumerator()
        {
            return new MessageQueueEnumerator(null);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.GetMessageQueueEnumerator1"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Creates an enumerator object for the message queues
        ///       available on the network.
        ///    </para>
        /// </devdoc>
        public static MessageQueueEnumerator GetMessageQueueEnumerator(MessageQueueCriteria criteria)
        {
            return new MessageQueueEnumerator(criteria);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.GetMessageEnumerator"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Creates an enumerator object for the messages in the queue. Superceded by GetMessageEnumerator2.
        ///    </para>
        /// </devdoc>
        [Obsolete("This method returns a MessageEnumerator that implements RemoveCurrent family of methods incorrectly. Please use GetMessageEnumerator2 instead.")]
        public MessageEnumerator GetMessageEnumerator()
        {
            if (!peekGranted)
            {
                peekGranted = true;
            }

            return new MessageEnumerator(this, false);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.GetMessageEnumerator2"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Creates an enumerator object for the messages in the queue.
        ///    </para>
        /// </devdoc>
        public MessageEnumerator GetMessageEnumerator2()
        {
            if (!peekGranted)
            {
                peekGranted = true;
            }

            return new MessageEnumerator(this, true);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.GetPrivateQueuesByMachine"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Retrieves all the private queues on
        ///       the specified computer.
        ///    </para>
        /// </devdoc>
        public static MessageQueue[] GetPrivateQueuesByMachine(string machineName)
        {
            if (!SyntaxCheck.CheckMachineName(machineName))
            {
                throw new ArgumentException(Res.GetString(Res.InvalidParameter, "MachineName", machineName));
            }

            if (machineName == "." || (string.Compare(machineName, ComputerName, true, CultureInfo.InvariantCulture) == 0))
            {
                machineName = null;
            }

            MessagePropertyVariants properties = new(5, 0);
            properties.SetNull(NativeMethods.MANAGEMENT_PRIVATEQ);
            int status = UnsafeNativeMethods.MQMgmtGetInfo(machineName, "MACHINE", properties.Lock());
            properties.Unlock();
            if (IsFatalError(status))
            {
                throw new MessageQueueException(status);
            }

            uint len = properties.GetStringVectorLength(NativeMethods.MANAGEMENT_PRIVATEQ);
            IntPtr basePointer = properties.GetStringVectorBasePointer(NativeMethods.MANAGEMENT_PRIVATEQ);
            var queues = new MessageQueue[len];
            for (int index = 0; index < len; ++index)
            {
                IntPtr stringPointer = checked(Marshal.ReadIntPtr((IntPtr)((long)basePointer + (index * IntPtr.Size))));
                //Using Unicode API even on Win9x
                string path = Marshal.PtrToStringUni(stringPointer);
                queues[index] = new MessageQueue("FormatName:DIRECT=OS:" + path)
                {
                    queuePath = path
                };
                SafeNativeMethods.MQFreeMemory(stringPointer);
            }

            SafeNativeMethods.MQFreeMemory(basePointer);
            return queues;
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.GetPublicQueues"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Retrieves all public queues on the network.
        ///    </para>
        /// </devdoc>
        public static MessageQueue[] GetPublicQueues()
        {
            return CreateMessageQueuesSnapshot(null);
        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.GetPublicQueues1"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Retrieves a
        ///       set of public queues filtered by the specified criteria.
        ///    </para>
        /// </devdoc>
        public static MessageQueue[] GetPublicQueues(MessageQueueCriteria criteria)
        {
            return CreateMessageQueuesSnapshot(criteria);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.GetPublicQueuesByCategory"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Retrieves a
        ///       set of public queues filtered by the specified category.
        ///    </para>
        /// </devdoc>
        public static MessageQueue[] GetPublicQueuesByCategory(Guid category)
        {
            MessageQueueCriteria criteria = new()
            {
                Category = category
            };
            return CreateMessageQueuesSnapshot(criteria);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.GetPublicQueuesByLabel"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Retrieves a
        ///       set of public queues filtered by the specified label.
        ///    </para>
        /// </devdoc>
        public static MessageQueue[] GetPublicQueuesByLabel(string label)
        {
            MessageQueueCriteria criteria = new()
            {
                Label = label
            };
            return CreateMessageQueuesSnapshot(criteria);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.GetPublicQueuesByMachine"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Retrieves all public queues on the specified computer.
        ///    </para>
        /// </devdoc>
        public static MessageQueue[] GetPublicQueuesByMachine(string machineName)
        {
            if (!SyntaxCheck.CheckMachineName(machineName))
            {
                throw new ArgumentException(Res.GetString(Res.InvalidParameter, "MachineName", machineName));
            }

            MessageQueueCriteria criteria = new()
            {
                MachineName = machineName
            };
            return CreateMessageQueuesSnapshot(criteria);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.IsCanonicalPath"]/*' />
        /// <internalonly/>
        static bool IsCanonicalPath(string path, bool checkQueueNameSize)
        {
            if (!ValidatePath(path, checkQueueNameSize))
            {
                return false;
            }

            string upperPath = path.ToUpper(CultureInfo.InvariantCulture);
            if (upperPath.StartsWith(PREFIX_LABEL) ||
                upperPath.StartsWith(PREFIX_FORMAT_NAME) ||
                upperPath.EndsWith(SUFIX_DEADLETTER) ||
                upperPath.EndsWith(SUFIX_DEADXACT) ||
                upperPath.EndsWith(SUFIX_JOURNAL))
            {
                return false;
            }

            return true;
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.IsFatalError"]/*' />
        /// <internalonly/>
        internal static bool IsFatalError(int value)
        {
            bool isSuccessful = value == 0x00000000;
            bool isInformation = (value & unchecked((int)0xC0000000)) == 0x40000000;
            return !isInformation && !isSuccessful;
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.IsMemoryError"]/*' />
        /// <internalonly/>
        internal static bool IsMemoryError(int value)
        {
            if (value is ((int)MessageQueueErrorCode.BufferOverflow) or
                 ((int)MessageQueueErrorCode.LabelBufferTooSmall) or
                 ((int)MessageQueueErrorCode.ProviderNameBufferTooSmall) or
                 ((int)MessageQueueErrorCode.SenderCertificateBufferTooSmall) or
                 ((int)MessageQueueErrorCode.SenderIdBufferTooSmall) or
                 ((int)MessageQueueErrorCode.SecurityDescriptorBufferTooSmall) or
                 ((int)MessageQueueErrorCode.SignatureBufferTooSmall) or
                 ((int)MessageQueueErrorCode.SymmetricKeyBufferTooSmall) or
                 ((int)MessageQueueErrorCode.UserBufferTooSmall) or
                 ((int)MessageQueueErrorCode.FormatNameBufferTooSmall))
            {
                return true;
            }

            return false;
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.OnRequestCompleted"]/*' />
        /// <devdoc>
        ///    Used for component model event support.
        /// </devdoc>
        /// <internalonly/>
        void OnRequestCompleted(IAsyncResult asyncResult)
        {
            if (((AsynchronousRequest)asyncResult).Action == NativeMethods.QUEUE_ACTION_PEEK_CURRENT)
            {
                if (onPeekCompleted != null)
                {
                    PeekCompletedEventArgs eventArgs = new(this, asyncResult);
                    onPeekCompleted(this, eventArgs);
                }
            }
            else
            {
                if (onReceiveCompleted != null)
                {
                    ReceiveCompletedEventArgs eventArgs = new(this, asyncResult);
                    onReceiveCompleted(this, eventArgs);
                }
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Peek"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Returns without removing (peeks) the first message
        ///       available in the queue referenced by the <see cref='System.Messaging.MessageQueue'/> . This call
        ///       is synchronous. It
        ///       blocks the current
        ///       thread of execution until a message is
        ///       available.
        ///    </para>
        /// </devdoc>
        public Message Peek()
        {
            return ReceiveCurrent(InfiniteTimeout, NativeMethods.QUEUE_ACTION_PEEK_CURRENT, CursorHandle.NullHandle, MessageReadPropertyFilter, null, MessageQueueTransactionType.None);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Peek1"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Returns without removing (peeks) the first message
        ///       available in the queue referenced by the <see cref='System.Messaging.MessageQueue'/>
        ///       . Waits
        ///       the specified interval for a message to become
        ///       available.
        ///    </para>
        /// </devdoc>
        public Message Peek(TimeSpan timeout)
        {
            return ReceiveCurrent(timeout, NativeMethods.QUEUE_ACTION_PEEK_CURRENT, CursorHandle.NullHandle, MessageReadPropertyFilter, null, MessageQueueTransactionType.None);
        }


        public Message Peek(TimeSpan timeout, Cursor cursor, PeekAction action)
        {
            if (action is not PeekAction.Current and not PeekAction.Next)
            {
                throw new ArgumentOutOfRangeException(Res.GetString(Res.InvalidParameter, "action", action.ToString()));
            }

            ArgumentNullException.ThrowIfNull(cursor);

            return ReceiveCurrent(timeout, (int)action, cursor.Handle, MessageReadPropertyFilter, null, MessageQueueTransactionType.None);
        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.PeekById"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Peeks the message that matches the given ID.
        ///       If there is no message with a matching ID,
        ///       an exception will be raised.
        ///    </para>
        /// </devdoc>
        public Message PeekById(string id)
        {
            return ReceiveBy(id, TimeSpan.Zero, false, true, false, null, MessageQueueTransactionType.None);
        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.PeekById1"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Peeks the message that matches the
        ///       given ID. This method waits until a message with
        ///       a matching ID is available, or the given timeout
        ///       expires when no more messages can be
        ///       inspected.
        ///    </para>
        /// </devdoc>
        public Message PeekById(string id, TimeSpan timeout)
        {
            return ReceiveBy(id, timeout, false, true, true, null, MessageQueueTransactionType.None);
        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.PeekByCorrelationId"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Peeks the message that matches the
        ///       given correlation ID. If there is no message with
        ///       a matching correlation ID, an exception is
        ///       thrown.
        ///    </para>
        /// </devdoc>
        public Message PeekByCorrelationId(string correlationId)
        {
            return ReceiveBy(correlationId, TimeSpan.Zero, false, false, false, null, MessageQueueTransactionType.None);
        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.PeekByCorrelationId1"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Peeks the message that matches the
        ///       given correlation ID. This function will wait
        ///       until a message with a matching correlation ID is
        ///       available, or the given timeout expires when
        ///       no more messages can be inspected.
        ///    </para>
        /// </devdoc>
        public Message PeekByCorrelationId(string correlationId, TimeSpan timeout)
        {
            return ReceiveBy(correlationId, timeout, false, false, true, null, MessageQueueTransactionType.None);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Purge"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Deletes all the messages contained in the queue.
        ///    </para>
        /// </devdoc>
        public void Purge()
        {
            if (!receiveGranted)
            {
                receiveGranted = true;
            }

            int status = StaleSafePurgeQueue();
            if (IsFatalError(status))
            {
                throw new MessageQueueException(status);
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Receive"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Receives the first message available in the queue referenced by the <see cref='System.Messaging.MessageQueue'/> . This
        ///       call is synchronous. It blocks the current thread of execution until a message is
        ///       available.
        ///    </para>
        /// </devdoc>
        public Message Receive()
        {
            return ReceiveCurrent(InfiniteTimeout, NativeMethods.QUEUE_ACTION_RECEIVE, CursorHandle.NullHandle, MessageReadPropertyFilter, null, MessageQueueTransactionType.None);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Receive1"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Receives the first message available in the queue referenced by the <see cref='System.Messaging.MessageQueue'/> . This
        ///       call is synchronous. It blocks the current thread of execution until a message is
        ///       available.
        ///    </para>
        /// </devdoc>
        public Message Receive(MessageQueueTransaction transaction)
        {
            ArgumentNullException.ThrowIfNull(transaction);

            return ReceiveCurrent(InfiniteTimeout, NativeMethods.QUEUE_ACTION_RECEIVE, CursorHandle.NullHandle, MessageReadPropertyFilter, transaction, MessageQueueTransactionType.None);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Receive5"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public Message Receive(MessageQueueTransactionType transactionType)
        {
            if (!ValidationUtility.ValidateMessageQueueTransactionType(transactionType))
            {
                throw new InvalidEnumArgumentException("transactionType", (int)transactionType, typeof(MessageQueueTransactionType));
            }

            return ReceiveCurrent(InfiniteTimeout, NativeMethods.QUEUE_ACTION_RECEIVE, CursorHandle.NullHandle, MessageReadPropertyFilter, null, transactionType);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Receive2"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Receives the first message available in the queue
        ///       referenced by the <see cref='System.Messaging.MessageQueue'/>
        ///       . Waits the specified interval for a message to become
        ///       available.
        ///    </para>
        /// </devdoc>
        public Message Receive(TimeSpan timeout)
        {
            return ReceiveCurrent(timeout, NativeMethods.QUEUE_ACTION_RECEIVE, CursorHandle.NullHandle, MessageReadPropertyFilter, null, MessageQueueTransactionType.None);
        }


        public Message Receive(TimeSpan timeout, Cursor cursor)
        {
            ArgumentNullException.ThrowIfNull(cursor);

            return ReceiveCurrent(timeout, NativeMethods.QUEUE_ACTION_RECEIVE, cursor.Handle, MessageReadPropertyFilter, null, MessageQueueTransactionType.None);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Receive3"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Receives the first message available in the queue
        ///       referenced by the <see cref='System.Messaging.MessageQueue'/>
        ///       . Waits the specified interval for a message to become
        ///       available.
        ///    </para>
        /// </devdoc>
        public Message Receive(TimeSpan timeout, MessageQueueTransaction transaction)
        {
            ArgumentNullException.ThrowIfNull(transaction);

            return ReceiveCurrent(timeout, NativeMethods.QUEUE_ACTION_RECEIVE, CursorHandle.NullHandle, MessageReadPropertyFilter, transaction, MessageQueueTransactionType.None);
        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Receive4"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public Message Receive(TimeSpan timeout, MessageQueueTransactionType transactionType)
        {
            if (!ValidationUtility.ValidateMessageQueueTransactionType(transactionType))
            {
                throw new InvalidEnumArgumentException("transactionType", (int)transactionType, typeof(MessageQueueTransactionType));
            }

            return ReceiveCurrent(timeout, NativeMethods.QUEUE_ACTION_RECEIVE, CursorHandle.NullHandle, MessageReadPropertyFilter, null, transactionType);
        }


        public Message Receive(TimeSpan timeout, Cursor cursor, MessageQueueTransaction transaction)
        {
            ArgumentNullException.ThrowIfNull(transaction);

            ArgumentNullException.ThrowIfNull(cursor);

            return ReceiveCurrent(timeout, NativeMethods.QUEUE_ACTION_RECEIVE, cursor.Handle, MessageReadPropertyFilter, transaction, MessageQueueTransactionType.None);
        }



        public Message Receive(TimeSpan timeout, Cursor cursor, MessageQueueTransactionType transactionType)
        {
            if (!ValidationUtility.ValidateMessageQueueTransactionType(transactionType))
            {
                throw new InvalidEnumArgumentException("transactionType", (int)transactionType, typeof(MessageQueueTransactionType));
            }

            ArgumentNullException.ThrowIfNull(cursor);

            return ReceiveCurrent(timeout, NativeMethods.QUEUE_ACTION_RECEIVE, cursor.Handle, MessageReadPropertyFilter, null, transactionType);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ReceiveAsync"]/*' />
        /// <internalonly/>
        unsafe AsynchronousRequest ReceiveAsync(TimeSpan timeout, CursorHandle cursorHandle, int action, AsyncCallback callback, object stateObject)
        {
            long timeoutInMilliseconds = (long)timeout.TotalMilliseconds;
            if (timeoutInMilliseconds is < 0 or > uint.MaxValue)
            {
                throw new ArgumentException(Res.GetString(Res.InvalidParameter, "timeout", timeout.ToString()));
            }

            if (action == NativeMethods.QUEUE_ACTION_RECEIVE)
            {
                if (!receiveGranted)
                {
                    receiveGranted = true;
                }
            }
            else
            {
                if (!peekGranted)
                {
                    peekGranted = true;
                }
            }

            if (!attached)
            {
                lock (this)
                {
                    if (!attached)
                    {
                        MessageQueueHandle handle = MQInfo.ReadHandle;
                        // If GetHandleInformation returns false, it means that the
                        // handle created for reading is not a File handle.
                        if (!SafeNativeMethods.GetHandleInformation(handle, out int handleInformation))
                        {
                            // If not a File handle, need to use MSMQ
                            // APC based async IO.
                            // We will need to store references to pending async requests (bug 88607)
                            useThreadPool = false;
                        }
                        else
                        {
                            // File handle can use IOCompletion ports
                            // since it only happens for NT
                            MQInfo.BindToThreadPool();
                            useThreadPool = true;
                        }
                        attached = true;
                    }
                }
            }

            if (callback == null)
            {
                onRequestCompleted ??= new AsyncCallback(OnRequestCompleted);

                callback = onRequestCompleted;
            }

            AsynchronousRequest request = new(this, (uint)timeoutInMilliseconds, cursorHandle, action, useThreadPool, stateObject, callback);

            //
            // Bug 88607 - keep a reference to outstanding asyncresult so its' not GCed
            //  This applies when GetHandleInformation returns false -> useThreadPool set to false
            //  It should only happen on dependent client, but we here we cover all GetHandleInformation
            //  failure paths for robustness.
            //
            // Need to add reference before calling BeginRead because request can complete by the time
            // reference is added, and it will be leaked if added to table after completion
            //
            if (!useThreadPool)
            {
                OutstandingAsyncRequests[request] = request;
            }

            request.BeginRead();

            return request;
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ReceiveBy"]/*' />
        /// <internalonly/>
        Message ReceiveBy(string id, TimeSpan timeout, bool remove, bool compareId, bool throwTimeout, MessageQueueTransaction transaction, MessageQueueTransactionType transactionType)
        {
            ArgumentNullException.ThrowIfNull(id);

            if (timeout < TimeSpan.Zero || timeout > InfiniteTimeout)
            {
                throw new ArgumentException(Res.GetString(Res.InvalidParameter, "timeout", timeout.ToString()));
            }

            MessagePropertyFilter oldFilter = receiveFilter;

            CursorHandle cursorHandle = null;
            try
            {
                receiveFilter = new MessagePropertyFilter();
                receiveFilter.ClearAll();
                if (!compareId)
                {
                    receiveFilter.CorrelationId = true;
                }
                else
                {
                    receiveFilter.Id = true;
                }

                //
                // Use cursor (and not MessageEnumerator) to navigate the queue because enumerator implementation can be incorrect
                // in multithreaded scenarios (see bug 329311)
                //

                //
                // Get cursor handle
                //
                int status = SafeNativeMethods.MQCreateCursor(MQInfo.ReadHandle, out cursorHandle);
                if (IsFatalError(status))
                {
                    throw new MessageQueueException(status);
                }

                try
                {
                    //
                    // peek first message in the queue
                    //
                    Message message = ReceiveCurrent(timeout, NativeMethods.QUEUE_ACTION_PEEK_CURRENT, cursorHandle,
                                                        MessageReadPropertyFilter, null, MessageQueueTransactionType.None);

                    while (message != null)
                    {
                        if ((compareId && string.Compare(message.Id, id, true, CultureInfo.InvariantCulture) == 0) ||
                            (!compareId && string.Compare(message.CorrelationId, id, true, CultureInfo.InvariantCulture) == 0))
                        {
                            //
                            // Found matching message, receive it and return
                            //
                            receiveFilter = oldFilter;

                            if (remove)
                            {
                                if (transaction == null)
                                {
                                    return ReceiveCurrent(timeout, NativeMethods.QUEUE_ACTION_RECEIVE, cursorHandle,
                                                          MessageReadPropertyFilter, null, transactionType);
                                }
                                else
                                {
                                    return ReceiveCurrent(timeout, NativeMethods.QUEUE_ACTION_RECEIVE, cursorHandle,
                                                          MessageReadPropertyFilter, transaction, MessageQueueTransactionType.None);
                                }
                            }
                            else
                            {
                                return ReceiveCurrent(timeout, NativeMethods.QUEUE_ACTION_PEEK_CURRENT, cursorHandle,
                                                      MessageReadPropertyFilter, null, MessageQueueTransactionType.None);
                            }
                        } //end if

                        //
                        // Continue search, peek next message
                        //
                        message = ReceiveCurrent(timeout, NativeMethods.QUEUE_ACTION_PEEK_NEXT, cursorHandle,
                                                        MessageReadPropertyFilter, null, MessageQueueTransactionType.None);


                    }

                }
                catch (MessageQueueException)
                {
                    // don't do anything, just use this catch as convenient means to exit the search
                }
            }
            finally
            {
                receiveFilter = oldFilter;
                cursorHandle?.Close();
            }

            if (!throwTimeout)
            {
                throw new InvalidOperationException(Res.GetString(Res.MessageNotFound));
            }
            else
            {
                throw new MessageQueueException((int)MessageQueueErrorCode.IOTimeout);
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ReceiveById"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Receives the message that matches the given
        ///       ID. If there is no message with a matching
        ///       ID, an exception is thrown.
        ///    </para>
        /// </devdoc>
        public Message ReceiveById(string id)
        {
            return ReceiveBy(id, TimeSpan.Zero, true, true, false, null, MessageQueueTransactionType.None);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ReceiveById1"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Receives the message that matches the given
        ///       ID. If there is no message with a matching
        ///       ID, an exception is thrown.
        ///    </para>
        /// </devdoc>
        public Message ReceiveById(string id, MessageQueueTransaction transaction)
        {
            ArgumentNullException.ThrowIfNull(transaction);

            return ReceiveBy(id, TimeSpan.Zero, true, true, false, transaction, MessageQueueTransactionType.None);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ReceiveById5"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Receives the message that matches the given
        ///       ID. If there is no message with a matching
        ///       ID, an exception is thrown.
        ///    </para>
        /// </devdoc>
        public Message ReceiveById(string id, MessageQueueTransactionType transactionType)
        {
            if (!ValidationUtility.ValidateMessageQueueTransactionType(transactionType))
            {
                throw new InvalidEnumArgumentException("transactionType", (int)transactionType, typeof(MessageQueueTransactionType));
            }

            return ReceiveBy(id, TimeSpan.Zero, true, true, false, null, transactionType);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ReceiveById2"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Receives the message that matches the given
        ///       ID. This method waits until a message with
        ///       a matching id is available or the given timeout
        ///       expires when no more messages can be
        ///       inspected.
        ///    </para>
        /// </devdoc>
        public Message ReceiveById(string id, TimeSpan timeout)
        {
            return ReceiveBy(id, timeout, true, true, true, null, MessageQueueTransactionType.None);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ReceiveById3"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Receives the message that matches the given
        ///       ID. This method waits until a message with
        ///       a matching id is available or the given timeout
        ///       expires when no more messages can be
        ///       inspected.
        ///    </para>
        /// </devdoc>
        public Message ReceiveById(string id, TimeSpan timeout, MessageQueueTransaction transaction)
        {
            ArgumentNullException.ThrowIfNull(transaction);

            return ReceiveBy(id, timeout, true, true, true, transaction, MessageQueueTransactionType.None);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ReceiveById4"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Receives the message that matches the given
        ///       ID. This method waits until a message with
        ///       a matching id is available or the given timeout
        ///       expires when no more messages can be
        ///       inspected.
        ///    </para>
        /// </devdoc>
        public Message ReceiveById(string id, TimeSpan timeout, MessageQueueTransactionType transactionType)
        {
            if (!ValidationUtility.ValidateMessageQueueTransactionType(transactionType))
            {
                throw new InvalidEnumArgumentException("transactionType", (int)transactionType, typeof(MessageQueueTransactionType));
            }

            return ReceiveBy(id, timeout, true, true, true, null, transactionType);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ReceiveByCorrelationId"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Receivess the message that matches the
        ///       given correlation ID. If there is no message with
        ///       a matching correlation ID, an exception is
        ///       thrown.
        ///    </para>
        /// </devdoc>
        public Message ReceiveByCorrelationId(string correlationId)
        {
            return ReceiveBy(correlationId, TimeSpan.Zero, true, false, false, null, MessageQueueTransactionType.None);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ReceiveByCorrelationId1"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Receivess the message that matches the
        ///       given correlation ID. If there is no message with
        ///       a matching correlation ID, an exception is
        ///       thrown.
        ///    </para>
        /// </devdoc>
        public Message ReceiveByCorrelationId(string correlationId, MessageQueueTransaction transaction)
        {
            ArgumentNullException.ThrowIfNull(transaction);

            return ReceiveBy(correlationId, TimeSpan.Zero, true, false, false, transaction, MessageQueueTransactionType.None);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ReceiveByCorrelationId5"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Receivess the message that matches the
        ///       given correlation ID. If there is no message with
        ///       a matching correlation ID, an exception is
        ///       thrown.
        ///    </para>
        /// </devdoc>
        public Message ReceiveByCorrelationId(string correlationId, MessageQueueTransactionType transactionType)
        {
            if (!ValidationUtility.ValidateMessageQueueTransactionType(transactionType))
            {
                throw new InvalidEnumArgumentException("transactionType", (int)transactionType, typeof(MessageQueueTransactionType));
            }

            return ReceiveBy(correlationId, TimeSpan.Zero, true, false, false, null, transactionType);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ReceiveByCorrelationId2"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Receives the message that matches
        ///       the given correlation ID. This method waits
        ///       until a message with a matching correlation ID is
        ///       available or the given timeout expires when
        ///       no more messages can be inspected.
        ///    </para>
        /// </devdoc>
        public Message ReceiveByCorrelationId(string correlationId, TimeSpan timeout)
        {
            return ReceiveBy(correlationId, timeout, true, false, true, null, MessageQueueTransactionType.None);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ReceiveByCorrelationId3"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Receives the message that matches
        ///       the given correlation ID. This method waits
        ///       until a message with a matching correlation ID is
        ///       available or the given timeout expires when
        ///       no more messages can be inspected.
        ///    </para>
        /// </devdoc>
        public Message ReceiveByCorrelationId(string correlationId, TimeSpan timeout, MessageQueueTransaction transaction)
        {
            ArgumentNullException.ThrowIfNull(transaction);

            return ReceiveBy(correlationId, timeout, true, false, true, transaction, MessageQueueTransactionType.None);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ReceiveByCorrelationId4"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Receives the message that matches
        ///       the given correlation ID. This method waits
        ///       until a message with a matching correlation ID is
        ///       available or the given timeout expires when
        ///       no more messages can be inspected.
        ///    </para>
        /// </devdoc>
        public Message ReceiveByCorrelationId(string correlationId, TimeSpan timeout, MessageQueueTransactionType transactionType)
        {
            if (!ValidationUtility.ValidateMessageQueueTransactionType(transactionType))
            {
                throw new InvalidEnumArgumentException("transactionType", (int)transactionType, typeof(MessageQueueTransactionType));
            }

            return ReceiveBy(correlationId, timeout, true, false, true, null, transactionType);
        }


        public Message ReceiveByLookupId(long lookupId)
        {
            return InternalReceiveByLookupId(true, MessageLookupAction.Current, lookupId, null, MessageQueueTransactionType.None);
        }

        public Message ReceiveByLookupId(MessageLookupAction action, long lookupId, MessageQueueTransactionType transactionType)
        {
            return InternalReceiveByLookupId(true, action, lookupId, null, transactionType);
        }


        public Message ReceiveByLookupId(MessageLookupAction action, long lookupId, MessageQueueTransaction transaction)
        {
            return InternalReceiveByLookupId(true, action, lookupId, transaction, MessageQueueTransactionType.None);
        }

        public Message PeekByLookupId(long lookupId)
        {
            return InternalReceiveByLookupId(false, MessageLookupAction.Current, lookupId, null, MessageQueueTransactionType.None);
        }

        public Message PeekByLookupId(MessageLookupAction action, long lookupId)
        {
            return InternalReceiveByLookupId(false, action, lookupId, null, MessageQueueTransactionType.None);
        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.InternalReceiveByLookupId"]/*' />
        /// <internalonly/>
        internal unsafe Message InternalReceiveByLookupId(bool receive, MessageLookupAction lookupAction, long lookupId,
            MessageQueueTransaction internalTransaction, MessageQueueTransactionType transactionType)
        {

            if (!ValidationUtility.ValidateMessageQueueTransactionType(transactionType))
            {
                throw new InvalidEnumArgumentException("transactionType", (int)transactionType, typeof(MessageQueueTransactionType));
            }

            if (!ValidationUtility.ValidateMessageLookupAction(lookupAction))
            {
                throw new InvalidEnumArgumentException("action", (int)lookupAction, typeof(MessageLookupAction));
            }

            if (!Msmq3OrNewer)
            {
                throw new PlatformNotSupportedException(Res.GetString(Res.PlatformNotSupported));
            }

            int action;

            if (receive)
            {
                if (!receiveGranted)
                {
                    receiveGranted = true;
                }

                action = NativeMethods.LOOKUP_RECEIVE_MASK | (int)lookupAction;

            }
            else
            {
                if (!peekGranted)
                {
                    peekGranted = true;
                }

                action = NativeMethods.LOOKUP_PEEK_MASK | (int)lookupAction;
            }


            MessagePropertyFilter filter = MessageReadPropertyFilter;


            int status = 0;
            Message receiveMessage = null;
            MessagePropertyVariants.MQPROPS lockedReceiveMessage = null;
            if (filter != null)
            {
                receiveMessage = new Message((MessagePropertyFilter)filter.Clone());
                receiveMessage.SetLookupId(lookupId);

                if (formatter != null)
                {
                    receiveMessage.Formatter = (IMessageFormatter)formatter.Clone();
                }

                lockedReceiveMessage = receiveMessage.Lock();
            }

            try
            {
                if ((internalTransaction != null) && receive)
                {
                    status = StaleSafeReceiveByLookupId(lookupId, action, lockedReceiveMessage, null, null, internalTransaction.BeginQueueOperation());
                }
                else
                {
                    status = StaleSafeReceiveByLookupId(lookupId, action, lockedReceiveMessage, null, null, (IntPtr)transactionType);
                }

                if (receiveMessage != null)
                {
                    //Need to keep trying until enough space has been allocated.
                    //Concurrent scenarions might not succeed on the second retry.
                    while (IsMemoryError(status))
                    {
                        receiveMessage.Unlock();
                        receiveMessage.AdjustMemory();
                        lockedReceiveMessage = receiveMessage.Lock();
                        if ((internalTransaction != null) && receive)
                        {
                            status = StaleSafeReceiveByLookupId(lookupId, action, lockedReceiveMessage, null, null, internalTransaction.InnerTransaction);
                        }
                        else
                        {
                            status = StaleSafeReceiveByLookupId(lookupId, action, lockedReceiveMessage, null, null, (IntPtr)transactionType);
                        }
                    }

                    receiveMessage.Unlock();
                }
            }
            finally
            {
                if ((internalTransaction != null) && receive)
                {
                    internalTransaction.EndQueueOperation();
                }
            }

            if (status == (int)MessageQueueErrorCode.MessageNotFound)
            {
                throw new InvalidOperationException(Res.GetString(Res.MessageNotFound));
            }

            if (IsFatalError(status))
            {
                throw new MessageQueueException(status);
            }

            return receiveMessage;
        }



        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ReceiveCurrent"]/*' />
        /// <internalonly/>
        internal unsafe Message ReceiveCurrent(TimeSpan timeout, int action, CursorHandle cursor, MessagePropertyFilter filter, MessageQueueTransaction internalTransaction, MessageQueueTransactionType transactionType)
        {
            long timeoutInMilliseconds = (long)timeout.TotalMilliseconds;
            if (timeoutInMilliseconds is < 0 or > uint.MaxValue)
            {
                throw new ArgumentException(Res.GetString(Res.InvalidParameter, "timeout", timeout.ToString()));
            }

            if (action == NativeMethods.QUEUE_ACTION_RECEIVE)
            {
                if (!receiveGranted)
                {
                    receiveGranted = true;
                }
            }
            else
            {
                if (!peekGranted)
                {
                    peekGranted = true;
                }
            }

            int status = 0;
            Message receiveMessage = null;
            MessagePropertyVariants.MQPROPS lockedReceiveMessage = null;
            if (filter != null)
            {
                receiveMessage = new Message((MessagePropertyFilter)filter.Clone());
                if (formatter != null)
                {
                    receiveMessage.Formatter = (IMessageFormatter)formatter.Clone();
                }

                lockedReceiveMessage = receiveMessage.Lock();
            }

            try
            {
                if (internalTransaction != null)
                {
                    status = StaleSafeReceiveMessage((uint)timeoutInMilliseconds, action, lockedReceiveMessage, null, null, cursor, internalTransaction.BeginQueueOperation());
                }
                else
                {
                    status = StaleSafeReceiveMessage((uint)timeoutInMilliseconds, action, lockedReceiveMessage, null, null, cursor, (IntPtr)transactionType);
                }

                if (receiveMessage != null)
                {
                    //Need to keep trying until enough space has been allocated.
                    //Concurrent scenarios might not succeed on the second retry.
                    while (IsMemoryError(status))
                    {
                        // Need to special-case retrying PeekNext after a buffer overflow
                        // by using PeekCurrent on retries since otherwise MSMQ will
                        // advance the cursor, skipping messages
                        if (action == NativeMethods.QUEUE_ACTION_PEEK_NEXT)
                        {
                            action = NativeMethods.QUEUE_ACTION_PEEK_CURRENT;
                        }

                        receiveMessage.Unlock();
                        receiveMessage.AdjustMemory();
                        lockedReceiveMessage = receiveMessage.Lock();
                        if (internalTransaction != null)
                        {
                            status = StaleSafeReceiveMessage((uint)timeoutInMilliseconds, action, lockedReceiveMessage, null, null, cursor, internalTransaction.InnerTransaction);
                        }
                        else
                        {
                            status = StaleSafeReceiveMessage((uint)timeoutInMilliseconds, action, lockedReceiveMessage, null, null, cursor, (IntPtr)transactionType);
                        }
                    }

                }
            }
            finally
            {
                receiveMessage?.Unlock();

                internalTransaction?.EndQueueOperation();
            }

            if (IsFatalError(status))
            {
                throw new MessageQueueException(status);
            }

            return receiveMessage;
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Refresh"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Refreshes the properties presented by the <see cref='System.Messaging.MessageQueue'/>
        ///       to reflect the current state of the
        ///       resource.
        ///    </para>
        /// </devdoc>
        //
        public void Refresh()
        {
            PropertyFilter.ClearAll();
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.SaveQueueProperties"]/*' />
        /// <internalonly/>
        void SaveQueueProperties()
        {
            if (!administerGranted)
            {
                administerGranted = true;
            }

            int status = UnsafeNativeMethods.MQSetQueueProperties(FormatName, Properties.Lock());
            Properties.Unlock();
            if (IsFatalError(status))
            {
                throw new MessageQueueException(status);
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Send"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Sends an object to the queue referenced by this <see cref='System.Messaging.MessageQueue'/>
        ///       . The object is serialized
        ///       using the formatter provided.
        ///    </para>
        /// </devdoc>
        public void Send(object obj)
        {
            SendInternal(obj, null, MessageQueueTransactionType.None);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Send1"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Sends an object to the queue referenced by this <see cref='System.Messaging.MessageQueue'/>
        ///       . The object is serialized
        ///       using the formatter provided.
        ///    </para>
        /// </devdoc>
        public void Send(object obj, MessageQueueTransaction transaction)
        {
            ArgumentNullException.ThrowIfNull(transaction);

            SendInternal(obj, transaction, MessageQueueTransactionType.None);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Send5"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Sends an object to the queue referenced by this <see cref='System.Messaging.MessageQueue'/>
        ///       . The object is serialized
        ///       using the formatter provided.
        ///    </para>
        /// </devdoc>
        public void Send(object obj, MessageQueueTransactionType transactionType)
        {
            if (!ValidationUtility.ValidateMessageQueueTransactionType(transactionType))
            {
                throw new InvalidEnumArgumentException("transactionType", (int)transactionType, typeof(MessageQueueTransactionType));
            }

            SendInternal(obj, null, transactionType);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Send2"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Sends an object to the queue referenced by this <see cref='System.Messaging.MessageQueue'/>.
        ///       The object will be serialized
        ///       using the formatter provided.
        ///    </para>
        /// </devdoc>
        public void Send(object obj, string label)
        {
            Send(obj, label, null, MessageQueueTransactionType.None);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Send3"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Sends an object to the queue referenced by this <see cref='System.Messaging.MessageQueue'/>.
        ///       The object will be serialized
        ///       using the formatter provided.
        ///    </para>
        /// </devdoc>
        public void Send(object obj, string label, MessageQueueTransaction transaction)
        {
            ArgumentNullException.ThrowIfNull(transaction);

            Send(obj, label, transaction, MessageQueueTransactionType.None);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.Send4"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Sends an object to the queue referenced by this <see cref='System.Messaging.MessageQueue'/>.
        ///       The object will be serialized
        ///       using the formatter provided.
        ///    </para>
        /// </devdoc>
        public void Send(object obj, string label, MessageQueueTransactionType transactionType)
        {
            if (!ValidationUtility.ValidateMessageQueueTransactionType(transactionType))
            {
                throw new InvalidEnumArgumentException("transactionType", (int)transactionType, typeof(MessageQueueTransactionType));
            }

            Send(obj, label, null, transactionType);
        }

        void Send(object obj, string label, MessageQueueTransaction transaction, MessageQueueTransactionType transactionType)
        {
            ArgumentNullException.ThrowIfNull(label);

            if (obj is Message message)
            {
                message.Label = label;
                SendInternal(obj, transaction, transactionType);
            }
            else
            {
                string oldLabel = DefaultPropertiesToSend.Label;
                try
                {
                    DefaultPropertiesToSend.Label = label;
                    SendInternal(obj, transaction, transactionType);
                }
                finally
                {
                    DefaultPropertiesToSend.Label = oldLabel;
                }
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.SendInternal"]/*' />
        /// <internalonly/>
        void SendInternal(object obj, MessageQueueTransaction internalTransaction, MessageQueueTransactionType transactionType)
        {
            if (!sendGranted)
            {
                sendGranted = true;
            }

            if (obj is not Message message)
            {
                message = DefaultPropertiesToSend.CachedMessage;
                message.Formatter = Formatter;
                message.Body = obj;
            }

            //Write cached properties and if message is being forwarded Clear queue specific properties
            int status = 0;
            message.AdjustToSend();
            MessagePropertyVariants.MQPROPS properties = message.Lock();
            try
            {
                if (internalTransaction != null)
                {
                    status = StaleSafeSendMessage(properties, internalTransaction.BeginQueueOperation());
                }
                else
                {
                    status = StaleSafeSendMessage(properties, (IntPtr)transactionType);
                }
            }
            finally
            {
                message.Unlock();

                internalTransaction?.EndQueueOperation();
            }

            if (IsFatalError(status))
            {
                throw new MessageQueueException(status);
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ResolveQueueFromLabel"]/*' />
        /// <internalonly/>
        static MessageQueue ResolveQueueFromLabel(string path, bool throwException)
        {
            MessageQueue[] queues = GetPublicQueuesByLabel(path[PREFIX_LABEL.Length..]);
            if (queues.Length == 0)
            {
                if (throwException)
                {
                    throw new InvalidOperationException(Res.GetString(Res.InvalidLabel, path[PREFIX_LABEL.Length..]));
                }

                return null;
            }
            else if (queues.Length > 1)
            {
                throw new InvalidOperationException(Res.GetString(Res.AmbiguousLabel, path[PREFIX_LABEL.Length..]));
            }

            return queues[0];
        }

        /// <internalonly/>
        static string ResolveFormatNameFromQueuePath(string queuePath, bool throwException)
        {
            string machine = queuePath[..queuePath.IndexOf('\\')];
            //The name includes the \\
            string name = queuePath[queuePath.IndexOf('\\')..];
            //Check for machine DeadLetter or Journal
            if (string.Compare(name, SUFIX_DEADLETTER, true, CultureInfo.InvariantCulture) == 0 ||
                string.Compare(name, SUFIX_DEADXACT, true, CultureInfo.InvariantCulture) == 0 ||
                string.Compare(name, SUFIX_JOURNAL, true, CultureInfo.InvariantCulture) == 0)
            {
                //Need to get the machine Id to construct the format name.

                if (machine.CompareTo(".") == 0)
                {
                    machine = ComputerName;
                }

                //Create a guid to get the right format.
                Guid machineId = GetMachineId(machine);
                StringBuilder newFormatName = new();
                //System format names:
                //MACHINE=guid;DEADXACT
                //MACHINE=guid;DEADLETTER
                //MACHINE=guid;JOURNAL
                newFormatName.Append("MACHINE=");
                newFormatName.Append(machineId.ToString());
                if (string.Compare(name, SUFIX_DEADXACT, true, CultureInfo.InvariantCulture) == 0)
                {
                    newFormatName.Append(";DEADXACT");
                }
                else if (string.Compare(name, SUFIX_DEADLETTER, true, CultureInfo.InvariantCulture) == 0)
                {
                    newFormatName.Append(";DEADLETTER");
                }
                else
                {
                    newFormatName.Append(";JOURNAL");
                }

                return newFormatName.ToString();
            }
            else
            {
                string realPath = queuePath;
                bool journal = false;
                if (queuePath.ToUpper(CultureInfo.InvariantCulture).EndsWith(SUFIX_JOURNAL))
                {
                    journal = true;
                    int lastIndex = realPath.LastIndexOf('\\');
                    realPath = realPath[..lastIndex];
                }

                int result;
                StringBuilder newFormatName = new(NativeMethods.MAX_LABEL_LEN);
                result = NativeMethods.MAX_LABEL_LEN;
                int status = SafeNativeMethods.MQPathNameToFormatName(realPath, newFormatName, ref result);
                if (status != 0)
                {
                    if (throwException)
                    {
                        throw new MessageQueueException(status);
                    }
                    else if (status == (int)MessageQueueErrorCode.IllegalQueuePathName)
                    {
                        throw new MessageQueueException(status);
                    }

                    return null;
                }

                if (journal)
                {
                    newFormatName.Append(";JOURNAL");
                }

                return newFormatName.ToString();
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ResetPermissions"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public void ResetPermissions()
        {
            if (!administerGranted)
            {
                administerGranted = true;
            }

            int result = UnsafeNativeMethods.MQSetQueueSecurity(FormatName, NativeMethods.DACL_SECURITY_INFORMATION, null);
            if (result != NativeMethods.MQ_OK)
            {
                throw new MessageQueueException(result);
            }
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.SetPermissions"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public void SetPermissions(string user, MessageQueueAccessRights rights)
        {
            ArgumentNullException.ThrowIfNull(user);

            SetPermissions(user, rights, AccessControlEntryType.Allow);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.SetPermissions1"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public void SetPermissions(string user, MessageQueueAccessRights rights, AccessControlEntryType entryType)
        {
            ArgumentNullException.ThrowIfNull(user);

            Trustee t = new(user);
            MessageQueueAccessControlEntry ace = new(t, rights, entryType);
            AccessControlList dacl = [ace];
            SetPermissions(dacl);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.SetPermissions2"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public void SetPermissions(MessageQueueAccessControlEntry ace)
        {
            ArgumentNullException.ThrowIfNull(ace);

            AccessControlList dacl = [ace];
            SetPermissions(dacl);
        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.SetPermissions3"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public void SetPermissions(AccessControlList dacl)
        {
            ArgumentNullException.ThrowIfNull(dacl);

            if (!administerGranted)
            {
                administerGranted = true;
            }

            //Access control is not supported in Win9x, need to check
            //the environment and take appropriate action.
            AccessControlList.CheckEnvironment();

            byte[] SecurityDescriptor = new byte[100];
            int mqResult;

            var sdHandle = GCHandle.Alloc(SecurityDescriptor, GCHandleType.Pinned);
            try
            {
                mqResult = UnsafeNativeMethods.MQGetQueueSecurity(FormatName,
                                                             NativeMethods.DACL_SECURITY_INFORMATION,
                                                             sdHandle.AddrOfPinnedObject(),
                                                             SecurityDescriptor.Length,
                                                             out int lengthNeeded);

                if (mqResult == NativeMethods.MQ_ERROR_SECURITY_DESCRIPTOR_TOO_SMALL)
                {
                    sdHandle.Free();
                    SecurityDescriptor = new byte[lengthNeeded];
                    sdHandle = GCHandle.Alloc(SecurityDescriptor, GCHandleType.Pinned);
                    mqResult = UnsafeNativeMethods.MQGetQueueSecurity(FormatName,
                                                                 NativeMethods.DACL_SECURITY_INFORMATION,
                                                                 sdHandle.AddrOfPinnedObject(),
                                                                 SecurityDescriptor.Length,
                                                                 out lengthNeeded);
                }

                if (mqResult != NativeMethods.MQ_OK)
                {
                    throw new MessageQueueException(mqResult);
                }

                bool success = UnsafeNativeMethods.GetSecurityDescriptorDacl(sdHandle.AddrOfPinnedObject(),
                                                                                out bool daclPresent,
                                                                                out nint pDacl,
                                                                                out bool daclDefaulted);

                if (!success)
                {
                    throw new Win32Exception();
                }

                // At this point we have the DACL for the queue.  Now we need to create
                // a new security descriptor with an updated DACL.

                NativeMethods.SECURITY_DESCRIPTOR newSecurityDescriptor = new();
                UnsafeNativeMethods.InitializeSecurityDescriptor(newSecurityDescriptor,
                                                                    NativeMethods.SECURITY_DESCRIPTOR_REVISION);
                IntPtr newDacl = dacl.MakeAcl(pDacl);
                try
                {
                    success = UnsafeNativeMethods.SetSecurityDescriptorDacl(newSecurityDescriptor,
                                                                               true,
                                                                               newDacl,
                                                                               false);

                    if (!success)
                    {
                        throw new Win32Exception();
                    }

                    int result = UnsafeNativeMethods.MQSetQueueSecurity(FormatName,
                                                                   NativeMethods.DACL_SECURITY_INFORMATION,
                                                                   newSecurityDescriptor);

                    if (result != NativeMethods.MQ_OK)
                    {
                        throw new MessageQueueException(result);
                    }
                }
                finally
                {
                    AccessControlList.FreeAcl(newDacl);
                }

                //If the format name has been cached, let's
                //remove it, since the process might no longer
                //have access to the corresponding queue.
                queueInfoCache.Remove(QueueInfoKey);
                formatNameCache.Remove(path.ToUpper(CultureInfo.InvariantCulture));
            }
            finally
            {
                if (sdHandle.IsAllocated)
                {
                    sdHandle.Free();
                }
            }

        }

        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.ValidatePath"]/*' />
        /// <internalonly/>
        internal static bool ValidatePath(string path, bool checkQueueNameSize)
        {
            if (path == null || path.Length == 0)
            {
                return true;
            }

            string upperPath = path.ToUpper(CultureInfo.InvariantCulture);
            if (upperPath.StartsWith(PREFIX_LABEL))
            {
                return true;
            }

            if (upperPath.StartsWith(PREFIX_FORMAT_NAME))
            {
                return true;
            }

            int number = 0;
            int index = -1;
            while (true)
            {
                int newIndex = upperPath.IndexOf('\\', index + 1);
                if (newIndex == -1)
                {
                    break;
                }
                else
                {
                    index = newIndex;
                }

                ++number;
            }

            if (number == 1)
            {
                if (checkQueueNameSize)
                {
                    long length = path.Length - (index + 1);
                    if (length > 255)
                    {
                        throw new ArgumentException(Res.GetString(Res.LongQueueName));
                    }
                }
                return true;
            }

            if (number == 2)
            {
                if (upperPath.EndsWith(SUFIX_JOURNAL))
                {
                    return true;
                }

                index = upperPath.LastIndexOf(SUFIX_PRIVATE + "\\");
                if (index != -1)
                {
                    return true;
                }
            }

            if (number == 3 && upperPath.EndsWith(SUFIX_JOURNAL))
            {
                index = upperPath.LastIndexOf(SUFIX_PRIVATE + "\\");
                if (index != -1)
                {
                    return true;
                }
            }

            return false;
        }


        /// <internalonly/>
        internal void SetAccessMode(QueueAccessMode accessMode)
        {
            //
            // this method should only be called from a constructor.
            // we don't support changing queue access mode after construction time.
            //
            if (!ValidationUtility.ValidateQueueAccessMode(accessMode))
            {
                throw new InvalidEnumArgumentException("accessMode", (int)accessMode, typeof(QueueAccessMode));
            }

            AccessMode = accessMode;
        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.QueuePropertyFilter"]/*' />
        /// <internalonly/>
        class QueuePropertyFilter
        {
            public bool Authenticate;
            public bool BasePriority;
            public bool CreateTime;
            public bool EncryptionLevel;
            public bool Id;
            // disable csharp compiler warning #0414: field assigned unused value
#pragma warning disable 0414
            public bool Transactional;
#pragma warning restore 0414
            public bool Label;
            public bool LastModifyTime;
            public bool MaximumJournalSize;
            public bool MaximumQueueSize;
            public bool MulticastAddress;
            // disable csharp compiler warning #0414: field assigned unused value
#pragma warning disable 0414
            public bool Path;
#pragma warning restore 0414
            public bool Category;
            public bool UseJournalQueue;

            public void ClearAll()
            {
                Authenticate = false;
                BasePriority = false;
                CreateTime = false;
                EncryptionLevel = false;
                Id = false;
                Transactional = false;
                Label = false;
                LastModifyTime = false;
                MaximumJournalSize = false;
                MaximumQueueSize = false;
                Path = false;
                Category = false;
                UseJournalQueue = false;
                MulticastAddress = false;
            }
        }


        /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.AsynchronousRequest"]/*' />
        /// <devdoc>
        ///    This class is used in asynchronous operations,
        ///    it keeps the context under which the asynchronous
        ///    request was posted.
        /// </devdoc>
        /// <internalonly/>
        class AsynchronousRequest : IAsyncResult
        {
            readonly IOCompletionCallback onCompletionStatusChanged;
            readonly SafeNativeMethods.ReceiveCallback onMessageReceived;
            readonly AsyncCallback callback;
            readonly ManualResetEvent resetEvent;
            readonly MessageQueue owner;
            int status = 0;
            Message message;
            readonly uint timeout;
            readonly CursorHandle cursorHandle;


            /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.AsynchronousRequest.AsynchronousRequest"]/*' />
            /// <devdoc>
            ///    Creates a new asynchronous request that
            ///    represents a pending asynchronous operation.
            /// </devdoc>
            /// <internalonly/>
            internal unsafe AsynchronousRequest(MessageQueue owner, uint timeout, CursorHandle cursorHandle, int action, bool useThreadPool, object asyncState, AsyncCallback callback)
            {
                this.owner = owner;
                AsyncState = asyncState;
                this.callback = callback;
                Action = action;
                this.timeout = timeout;
                resetEvent = new ManualResetEvent(false);
                this.cursorHandle = cursorHandle;

                if (!useThreadPool)
                {
                    onMessageReceived = new SafeNativeMethods.ReceiveCallback(OnMessageReceived);
                }
                else
                {
                    onCompletionStatusChanged = new IOCompletionCallback(OnCompletionStatusChanged);
                }
            }


            /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.AsynchronousRequest.Action"]/*' />
            /// <internalonly/>
            internal int Action { get; private set; }


            /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.AsynchronousRequest.AsyncState"]/*' />
            /// <devdoc>
            ///    IAsyncResult implementation
            /// </devdoc>
            public object AsyncState { get; }


            /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.AsynchronousRequest.AsyncWaitHandle"]/*' />
            /// <devdoc>
            ///    IAsyncResult implementation
            /// </devdoc>
            public WaitHandle AsyncWaitHandle => resetEvent;


            /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.AsynchronousRequest.CompletedSynchronously"]/*' />
            /// <devdoc>
            ///    IAsyncResult implementation
            /// </devdoc>
            public bool CompletedSynchronously => false;

            /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.AsynchronousRequest.IsCompleted"]/*' />
            /// <devdoc>
            ///    IAsyncResult implementation
            /// </devdoc>
            /// <internalonly/>
            public bool IsCompleted { get; private set; } = false;


            /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.AsynchronousRequest.BeginRead"]/*' />
            /// <devdoc>
            ///   Does the actual asynchronous receive posting.
            /// </devdoc>
            /// <internalonly/>
            internal unsafe void BeginRead()
            {
                NativeOverlapped* overlappedPointer = null;
                if (onCompletionStatusChanged != null)
                {
                    Overlapped overlapped = new()
                    {
                        AsyncResult = this
                    };
                    overlappedPointer = overlapped.Pack(onCompletionStatusChanged, null);
                }

                int localStatus = 0;
                message = new Message(owner.MessageReadPropertyFilter);

                try
                {
                    localStatus = owner.StaleSafeReceiveMessage(timeout, Action, message.Lock(), overlappedPointer, onMessageReceived, cursorHandle, IntPtr.Zero);
                    while (IsMemoryError(localStatus))
                    {
                        // Need to special-case retrying PeekNext after a buffer overflow
                        // by using PeekCurrent on retries since otherwise MSMQ will
                        // advance the cursor, skipping messages
                        if (Action == NativeMethods.QUEUE_ACTION_PEEK_NEXT)
                        {
                            Action = NativeMethods.QUEUE_ACTION_PEEK_CURRENT;
                        }

                        message.Unlock();
                        message.AdjustMemory();
                        localStatus = owner.StaleSafeReceiveMessage(timeout, Action, message.Lock(), overlappedPointer, onMessageReceived, cursorHandle, IntPtr.Zero);
                    }
                }
                catch (Exception)
                {
                    // Here will would do all the cleanup that RaiseCompletionEvent does on failure path,
                    // but without raising completion event.
                    // This is to preserve pre-Whidbey Beta 2 behavior, when exception thrown from this method
                    // would prevent RaiseCompletionEvent from being called (and also leak resources)
                    message.Unlock();

                    if (overlappedPointer != null)
                    {
                        Overlapped.Free(overlappedPointer);
                    }

                    if (!owner.useThreadPool)
                    {
                        owner.OutstandingAsyncRequests.Remove(this);
                    }

                    throw;
                }

                // NOTE: RaiseCompletionEvent is not in a finally block by design, for two reasons:
                // 1) the contract of BeginRead is to throw exception and not to notify event handler.
                // 2) we dont know what the value pf localStatus will be in case of exception
                if (IsFatalError(localStatus))
                {
                    RaiseCompletionEvent(localStatus, overlappedPointer);
                }
            }



            /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.AsynchronousRequest.End"]/*' />
            /// <devdoc>
            ///   Waits until the request has been completed.
            /// </devdoc>
            /// <internalonly/>
            internal Message End()
            {
                resetEvent.WaitOne();
                if (IsFatalError(status))
                {
                    throw new MessageQueueException(status);
                }

                if (owner.formatter != null)
                {
                    message.Formatter = (IMessageFormatter)owner.formatter.Clone();
                }

                return message;
            }

            /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.AsynchronousRequest.OnCompletionStatusChanged"]/*' />
            /// <devdoc>
            ///   Thread pool IOCompletionPort bound callback.
            /// </devdoc>
            /// <internalonly/>
            unsafe void OnCompletionStatusChanged(uint errorCode, uint numBytes, NativeOverlapped* overlappedPointer)
            {
                int result = 0;
                if (errorCode != 0)
                {
                    // MSMQ does a hacky trick to return the operation
                    // result through the completion port.

                    // eugenesh Dec 2004. Bug 419155:
                    // NativeOverlapped.InternalLow returns IntPtr, which is 64 bits on a 64 bit platform.
                    // It contains MSMQ error code, which, when set to an error value, is outside of the int range
                    // Therefore, OverflowException is thrown in checked context.
                    // However, IntPtr (int) operator ALWAYS runs in checked context on 64 bit platforms.
                    // Therefore, we first cast to long to avoid OverflowException, and then cast to int
                    // in unchecked context
                    long msmqError = overlappedPointer->InternalLow;
                    unchecked
                    {
                        result = (int)msmqError;
                    }
                }

                RaiseCompletionEvent(result, overlappedPointer);
            }


            /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.AsynchronousRequest.OnMessageReceived"]/*' />
            /// <devdoc>
            ///   MSMQ APC based callback.
            /// </devdoc>
            /// <internalonly/>
            unsafe void OnMessageReceived(int result, IntPtr handle, int timeout, int action, IntPtr propertiesPointer, NativeOverlapped* overlappedPointer, IntPtr cursorHandle)
            {
                RaiseCompletionEvent(result, overlappedPointer);
            }


            /// <include file='doc\MessageQueue.uex' path='docs/doc[@for="MessageQueue.AsynchronousRequest.RaiseCompletionEvent"]/*' />
            /// <internalonly/>
            // See comment explaining this SuppressMessage below
            unsafe void RaiseCompletionEvent(int result, NativeOverlapped* overlappedPointer)
            {

                if (IsMemoryError(result))
                {
                    while (IsMemoryError(result))
                    {
                        // Need to special-case retrying PeekNext after a buffer overflow
                        // by using PeekCurrent on retries since otherwise MSMQ will
                        // advance the cursor, skipping messages
                        if (Action == NativeMethods.QUEUE_ACTION_PEEK_NEXT)
                        {
                            Action = NativeMethods.QUEUE_ACTION_PEEK_CURRENT;
                        }

                        message.Unlock();
                        message.AdjustMemory();
                        try
                        {
                            // ReadHandle called from StaleSafeReceiveMessage can throw if the handle has been invalidated
                            // (for example, by closing it), and subsequent MQOpenQueue fails for some reason.
                            // Therefore catch exception (otherwise process will die) and propagate error
                            // EugeneSh Jan 2006 (Whidbey bug 570055)
                            result = owner.StaleSafeReceiveMessage(timeout, Action, message.Lock(), overlappedPointer, onMessageReceived, cursorHandle, IntPtr.Zero);
                        }
                        catch (MessageQueueException e)
                        {
                            result = (int)e.MessageQueueErrorCode;
                            break;
                        }
                    }

                    if (!IsFatalError(result))
                    {
                        return;
                    }
                }

                message.Unlock();

                if (IsCashedInfoInvalidOnReceive(result))
                {
                    owner.MQInfo.Close();
                    try
                    {
                        // For explanation of this try/catch, see comment above
                        result = owner.StaleSafeReceiveMessage(timeout, Action, message.Lock(), overlappedPointer, onMessageReceived, cursorHandle, IntPtr.Zero);
                    }
                    catch (MessageQueueException e)
                    {
                        result = (int)e.MessageQueueErrorCode;
                    }

                    if (!IsFatalError(result))
                    {
                        return;
                    }
                }

                status = result;
                if (overlappedPointer != null)
                {
                    Overlapped.Free(overlappedPointer);
                }

                IsCompleted = true;
                resetEvent.Set();

                try
                {
                    //
                    // 511878: The code below breaks the contract of ISynchronizeInvoke.
                    // We fixed it in 367076, but that fix resulted in a regression that is bug 511878.
                    // "Proper fix" for 511878 requires special-casing Form. That causes us to
                    // load System.Windows.Forms and System.Drawing,
                    // which were previously not loaded on this path.
                    // As only one customer complained about 367076, we decided to revert to
                    // Everett behavior
                    //
                    if (owner.SynchronizingObject != null &&
                        owner.SynchronizingObject.InvokeRequired)
                    {
                        owner.SynchronizingObject.BeginInvoke(callback, new object[] { this });
                    }
                    else
                    {
                        callback(this);
                    }
                }
                catch (Exception)
                {
                    // eugenesh, Dec 2004: Swallowing exceptions here is a serious bug.
                    // However, it would be a breaking change to remove this catch,
                    // therefore we decided to preserve the existing behavior

                }
                finally
                {
                    if (!owner.useThreadPool)
                    {
                        Debug.Assert(owner.OutstandingAsyncRequests.Contains(this));
                        owner.OutstandingAsyncRequests.Remove(this);
                    }
                }
            }
        }

        int StaleSafePurgeQueue()
        {
            int status = UnsafeNativeMethods.MQPurgeQueue(MQInfo.ReadHandle);
            if (status is ((int)MessageQueueErrorCode.StaleHandle) or ((int)MessageQueueErrorCode.QueueDeleted))
            {
                MQInfo.Close();
                status = UnsafeNativeMethods.MQPurgeQueue(MQInfo.ReadHandle);
            }
            return status;
        }

        int StaleSafeSendMessage(MessagePropertyVariants.MQPROPS properties, IntPtr transaction)
        {
            //
            // TransactionType.Automatic uses current System.Transactions transaction, if one is available;
            // otherwise, it passes Automatic to MSMQ to support COM+ transactions
            // NOTE: Need careful qualification of class names,
            // since ITransaction is defined by System.Messaging.Interop, System.Transactions and System.EnterpriseServices
            //
            if ((MessageQueueTransactionType)transaction == MessageQueueTransactionType.Automatic)
            {
                System.Transactions.Transaction tx = System.Transactions.Transaction.Current;
                if (tx != null)
                {
                    System.Transactions.IDtcTransaction ntx =
                        System.Transactions.TransactionInterop.GetDtcTransaction(tx);

                    return StaleSafeSendMessage(properties, (ITransaction)ntx);
                }
            }

            int status = UnsafeNativeMethods.MQSendMessage(MQInfo.WriteHandle, properties, transaction);
            if (status is ((int)MessageQueueErrorCode.StaleHandle) or ((int)MessageQueueErrorCode.QueueDeleted))
            {
                MQInfo.Close();
                status = UnsafeNativeMethods.MQSendMessage(MQInfo.WriteHandle, properties, transaction);
            }

            return status;
        }

        int StaleSafeSendMessage(MessagePropertyVariants.MQPROPS properties, ITransaction transaction)
        {
            int status = UnsafeNativeMethods.MQSendMessage(MQInfo.WriteHandle, properties, transaction);
            if (status is ((int)MessageQueueErrorCode.StaleHandle) or ((int)MessageQueueErrorCode.QueueDeleted))
            {
                MQInfo.Close();
                status = UnsafeNativeMethods.MQSendMessage(MQInfo.WriteHandle, properties, transaction);
            }
            return status;
        }

        internal unsafe int StaleSafeReceiveMessage(uint timeout, int action, MessagePropertyVariants.MQPROPS properties, NativeOverlapped* overlapped,
                                                                                           SafeNativeMethods.ReceiveCallback receiveCallback, CursorHandle cursorHandle, IntPtr transaction)
        {
            //
            // TransactionType.Automatic uses current System.Transactions transaction, if one is available;
            // otherwise, it passes Automatic to MSMQ to support COM+ transactions
            // NOTE: Need careful qualification of class names,
            // since ITransaction is defined by System.Messaging.Interop, System.Transactions and System.EnterpriseServices
            //
            if ((MessageQueueTransactionType)transaction == MessageQueueTransactionType.Automatic)
            {
                System.Transactions.Transaction tx = System.Transactions.Transaction.Current;
                if (tx != null)
                {
                    System.Transactions.IDtcTransaction ntx =
                        System.Transactions.TransactionInterop.GetDtcTransaction(tx);

                    return StaleSafeReceiveMessage(timeout, action, properties, overlapped, receiveCallback, cursorHandle, (ITransaction)ntx);
                }
            }

            int status = UnsafeNativeMethods.MQReceiveMessage(MQInfo.ReadHandle, timeout, action, properties, overlapped, receiveCallback, cursorHandle, transaction);
            if (IsCashedInfoInvalidOnReceive(status))
            {
                MQInfo.Close(); //invalidate cached ReadHandle, so it will be refreshed on next access
                status = UnsafeNativeMethods.MQReceiveMessage(MQInfo.ReadHandle, timeout, action, properties, overlapped, receiveCallback, cursorHandle, transaction);
            }
            return status;
        }

        unsafe int StaleSafeReceiveMessage(uint timeout, int action, MessagePropertyVariants.MQPROPS properties, NativeOverlapped* overlapped,
                                                                                           SafeNativeMethods.ReceiveCallback receiveCallback, CursorHandle cursorHandle, ITransaction transaction)
        {
            int status = UnsafeNativeMethods.MQReceiveMessage(MQInfo.ReadHandle, timeout, action, properties, overlapped, receiveCallback, cursorHandle, transaction);
            if (IsCashedInfoInvalidOnReceive(status))
            {
                MQInfo.Close(); //invalidate cached ReadHandle, so it will be refreshed on next access
                status = UnsafeNativeMethods.MQReceiveMessage(MQInfo.ReadHandle, timeout, action, properties, overlapped, receiveCallback, cursorHandle, transaction);
            }
            return status;
        }



        unsafe int StaleSafeReceiveByLookupId(long lookupId, int action, MessagePropertyVariants.MQPROPS properties,
            NativeOverlapped* overlapped, SafeNativeMethods.ReceiveCallback receiveCallback, IntPtr transaction)
        {

            if ((MessageQueueTransactionType)transaction == MessageQueueTransactionType.Automatic)
            {
                System.Transactions.Transaction tx = System.Transactions.Transaction.Current;
                if (tx != null)
                {
                    System.Transactions.IDtcTransaction ntx =
                        System.Transactions.TransactionInterop.GetDtcTransaction(tx);

                    return StaleSafeReceiveByLookupId(lookupId, action, properties, overlapped, receiveCallback, (ITransaction)ntx);
                }
            }

            int status = UnsafeNativeMethods.MQReceiveMessageByLookupId(MQInfo.ReadHandle, lookupId, action, properties, overlapped, receiveCallback, transaction);
            if (IsCashedInfoInvalidOnReceive(status))
            {
                MQInfo.Close(); //invalidate cached ReadHandle, so it will be refreshed on next access
                status = UnsafeNativeMethods.MQReceiveMessageByLookupId(MQInfo.ReadHandle, lookupId, action, properties, overlapped, receiveCallback, transaction);
            }
            return status;
        }


        unsafe int StaleSafeReceiveByLookupId(long lookupId, int action, MessagePropertyVariants.MQPROPS properties,
            NativeOverlapped* overlapped, SafeNativeMethods.ReceiveCallback receiveCallback, ITransaction transaction)
        {

            int status = UnsafeNativeMethods.MQReceiveMessageByLookupId(MQInfo.ReadHandle, lookupId, action, properties, overlapped, receiveCallback, transaction);
            if (IsCashedInfoInvalidOnReceive(status))
            {
                MQInfo.Close(); //invalidate cached ReadHandle, so it will be refreshed on next access
                status = UnsafeNativeMethods.MQReceiveMessageByLookupId(MQInfo.ReadHandle, lookupId, action, properties, overlapped, receiveCallback, transaction);
            }
            return status;
        }

        static bool IsCashedInfoInvalidOnReceive(int receiveResult)
        {
            // returns true if return code of ReceiveMessage indicates
            // that cached handle used for receive has become invalid
            return receiveResult is ((int)MessageQueueErrorCode.StaleHandle) or      //both qm and ac restarted
                    ((int)MessageQueueErrorCode.InvalidHandle) or    //get this if ac is not restarted
                    ((int)MessageQueueErrorCode.InvalidParameter); // get this on w2k
        }


        internal class CacheTable<Key, Value>
        {
            Dictionary<Key, CacheEntry<Value>> table;
            readonly ReaderWriterLock rwLock;

            // used for debugging
            readonly string name;

            // when the number of entries in the hashtable gets larger than capacity,
            // the "stale" entries are removed and capacity is reset to twice the number
            // of remaining entries
            int capacity;
            readonly int originalCapacity;

            // time, in seconds, after which an entry is considerred stale (if the reference
            // count is zero)
            readonly TimeSpan staleTime;

            public CacheTable(string name, int capacity, TimeSpan staleTime)
            {
                originalCapacity = capacity;
                this.capacity = capacity;
                this.staleTime = staleTime;
                this.name = name;
                rwLock = new ReaderWriterLock();
                table = [];
            }

            public Value Get(Key key)
            {
                Value val = default;    // This keyword might change with C# compiler
                rwLock.AcquireReaderLock(-1);
                try
                {
                    if (table.TryGetValue(key, out CacheEntry<Value> value))
                    {
                        CacheEntry<Value> entry = value;
                        if (entry != null)
                        {
                            entry.timeStamp = DateTime.UtcNow;
                            val = entry.contents;
                        }
                    }
                }
                finally
                {
                    rwLock.ReleaseReaderLock();
                }
                return val;
            }

            public void Put(Key key, Value val)
            {
                rwLock.AcquireWriterLock(-1);
                try
                {
                    if (val == null /* not Value.default - bug in C# compiler? */)
                    {
                        table[key] = null;
                    }
                    else
                    {
                        CacheEntry<Value> entry = null;
                        if (table.TryGetValue(key, out CacheEntry<Value> value))
                        {
                            entry = value; //which could be null also
                        }

                        if (entry == null)
                        {
                            entry = new CacheEntry<Value>();
                            table[key] = entry;
                            if (table.Count >= capacity)
                            {
                                ClearStale(staleTime);
                            }
                        }
                        entry.timeStamp = DateTime.UtcNow;
                        entry.contents = val;
                    }
                }
                finally
                {
                    rwLock.ReleaseWriterLock();
                }
            }

            public void Remove(Key key)
            {
                rwLock.AcquireWriterLock(-1);
                try
                {
                    table.Remove(key);
                }
                finally
                {
                    rwLock.ReleaseWriterLock();
                }
            }

            public void ClearStale(TimeSpan staleAge)
            {
                DateTime now = DateTime.UtcNow;
                Dictionary<Key, CacheEntry<Value>> newTable = [];

                rwLock.AcquireReaderLock(-1);
                try
                {
                    foreach (KeyValuePair<Key, CacheEntry<Value>> kv in table)
                    {
                        CacheEntry<Value> iterEntry = kv.Value;

                        // see if this entry is stale (ticks are 100 nano-sec.)
                        if (now - iterEntry.timeStamp < staleAge)
                        {
                            newTable[kv.Key] = kv.Value;
                        }
                    }
                }
                finally
                {
                    rwLock.ReleaseReaderLock();
                }

                rwLock.AcquireWriterLock(-1);
                table = newTable;
                capacity = 2 * table.Count;
                if (capacity < originalCapacity)
                {
                    capacity = originalCapacity;
                }

                rwLock.ReleaseWriterLock();
            }

            class CacheEntry<T>
            {
                public T contents;
                public DateTime timeStamp;
            }
        }

        internal class MQCacheableInfo : IDisposable
        {
            // Double-checked locking pattern requires volatile for read/write synchronization
            volatile MessageQueueHandle readHandle = MessageQueueHandle.InvalidHandle;

            // Double-checked locking pattern requires volatile for read/write synchronization
            volatile MessageQueueHandle writeHandle = MessageQueueHandle.InvalidHandle;
            bool isTransactional;

            // Double-checked locking pattern requires volatile for read/write synchronization
            volatile bool isTransactional_valid = false;

            // Double-checked locking pattern requires volatile for read/write synchronization
            volatile bool boundToThreadPool;
            readonly string formatName;
            readonly int shareMode;
            readonly QueueAccessModeHolder accessMode;
            bool disposed;

            readonly object syncRoot = new();

            public MQCacheableInfo(string formatName, QueueAccessMode accessMode, int shareMode)
            {
                this.formatName = formatName;
                this.shareMode = shareMode;

                // For each accessMode, corresponding QueueAccessModeHolder is a singleton.
                // Call factory method to return existing holder for this access mode,
                // or make a new one if noone used this access mode before.
                //
                this.accessMode = QueueAccessModeHolder.GetQueueAccessModeHolder(accessMode);
            }

            public bool CanRead
            {
                get
                {
                    if (!accessMode.CanRead())
                    {
                        return false;
                    }

                    if (readHandle.IsInvalid)
                    {
                        ObjectDisposedException.ThrowIf(disposed, GetType().Name);

                        lock (syncRoot)
                        {
                            if (readHandle.IsInvalid)
                            {
                                int status = UnsafeNativeMethods.MQOpenQueue(formatName, accessMode.GetReadAccessMode(), shareMode, out MessageQueueHandle result);
                                if (IsFatalError(status))
                                {
                                    return false;
                                }

                                readHandle = result;
                            }
                        }
                    }

                    return true;
                }
            }

            public bool CanWrite
            {
                get
                {
                    if (!accessMode.CanWrite())
                    {
                        return false;
                    }

                    if (writeHandle.IsInvalid)
                    {
                        ObjectDisposedException.ThrowIf(disposed, GetType().Name);

                        lock (syncRoot)
                        {
                            if (writeHandle.IsInvalid)
                            {
                                int status = UnsafeNativeMethods.MQOpenQueue(formatName, accessMode.GetWriteAccessMode(), 0, out MessageQueueHandle result);
                                if (IsFatalError(status))
                                {
                                    return false;
                                }

                                writeHandle = result;
                            }
                        }
                    }

                    return true;
                }
            }

            public int RefCount { get; private set; }

            public MessageQueueHandle ReadHandle
            {
                get
                {
                    if (readHandle.IsInvalid)
                    {
                        ObjectDisposedException.ThrowIf(disposed, GetType().Name);

                        lock (syncRoot)
                        {
                            if (readHandle.IsInvalid)
                            {
                                int status = UnsafeNativeMethods.MQOpenQueue(formatName, accessMode.GetReadAccessMode(), shareMode, out MessageQueueHandle result);
                                if (IsFatalError(status))
                                {
                                    throw new MessageQueueException(status);
                                }

                                readHandle = result;
                            }
                        }
                    }

                    return readHandle;
                }
            }

            public MessageQueueHandle WriteHandle
            {
                get
                {
                    if (writeHandle.IsInvalid)
                    {
                        ObjectDisposedException.ThrowIf(disposed, GetType().Name);

                        lock (syncRoot)
                        {
                            if (writeHandle.IsInvalid)
                            {
                                int status = UnsafeNativeMethods.MQOpenQueue(formatName, accessMode.GetWriteAccessMode(), 0, out MessageQueueHandle result);
                                if (IsFatalError(status))
                                {
                                    throw new MessageQueueException(status);
                                }

                                writeHandle = result;
                            }
                        }
                    }

                    return writeHandle;
                }
            }

            public bool Transactional
            {
                get
                {
                    if (!isTransactional_valid)
                    {
                        lock (syncRoot)
                        {
                            if (!isTransactional_valid)
                            {
                                QueuePropertyVariants props = new();
                                props.SetUI1(NativeMethods.QUEUE_PROPID_TRANSACTION, 0);
                                int status = UnsafeNativeMethods.MQGetQueueProperties(formatName, props.Lock());
                                props.Unlock();
                                if (IsFatalError(status))
                                {
                                    throw new MessageQueueException(status);
                                }

                                isTransactional = props.GetUI1(NativeMethods.QUEUE_PROPID_TRANSACTION) != NativeMethods.QUEUE_TRANSACTIONAL_NONE;
                                isTransactional_valid = true;
                            }
                        }
                    }

                    return isTransactional;
                }
            }

            public void AddRef()
            {
                lock (this)
                {
                    ++RefCount;
                }
            }

            public void BindToThreadPool()
            {
                if (!boundToThreadPool)
                {
                    lock (this)
                    {
                        if (!boundToThreadPool)
                        {
                            ThreadPool.BindHandle(ReadHandle);
                            boundToThreadPool = true;
                        }
                    }
                }
            }

            public void CloseIfNotReferenced()
            {
                lock (this)
                {
                    if (RefCount == 0)
                    {
                        Close();
                    }
                }
            }

            public void Close()
            {
                boundToThreadPool = false;
                if (!writeHandle.IsInvalid)
                {
                    lock (syncRoot)
                    {
                        if (!writeHandle.IsInvalid)
                        {
                            writeHandle.Close();
                        }
                    }
                }
                if (!readHandle.IsInvalid)
                {
                    lock (syncRoot)
                    {
                        if (!readHandle.IsInvalid)
                        {
                            readHandle.Close();
                        }
                    }
                }
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected virtual void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Close();
                }

                disposed = true;
            }

            public void Release()
            {
                lock (this)
                {
                    --RefCount;
                }
            }
        }

        internal class QueueInfoKeyHolder
        {
            readonly string formatName;
            readonly QueueAccessMode accessMode;

            public QueueInfoKeyHolder(string formatName, QueueAccessMode accessMode)
            {
                this.formatName = formatName.ToUpper(CultureInfo.InvariantCulture);
                this.accessMode = accessMode;
            }

            public override int GetHashCode()
            {
                return formatName.GetHashCode() + (int)accessMode;
            }

            public override bool Equals(object obj)
            {
                if (obj == null || GetType() != obj.GetType())
                {
                    return false;
                }

                var qik = (QueueInfoKeyHolder)obj;
                return Equals(qik);
            }

            public bool Equals(QueueInfoKeyHolder qik)
            {
                if (qik == null)
                {
                    return false;
                }

                // string.Equals performs case-sensitive and culture-insensitive comparison
                // we address case sensitivity by normalizing format name in the constructor
                return (accessMode == qik.accessMode) && formatName.Equals(qik.formatName);
            }
        }
    }
}

