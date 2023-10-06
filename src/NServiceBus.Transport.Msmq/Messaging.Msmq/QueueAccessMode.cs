//------------------------------------------------------------------------------
// <copyright file="QueueAccessMode.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Messaging.Msmq
{
    using System.Collections.Generic;
    using Messaging.Msmq.Interop;

    /// <include file='doc\QueueAccessMode.uex' path='docs/doc[@for="QueueAccessMode"]/*' />
    /// <devdoc>
    ///    <para>
    ///       Specifies what operations can be performed on the queue.
    ///    </para>
    /// </devdoc>
    public enum QueueAccessMode
    {
        Send = NativeMethods.QUEUE_ACCESS_SEND,
        Peek = NativeMethods.QUEUE_ACCESS_PEEK,
        Receive = NativeMethods.QUEUE_ACCESS_RECEIVE,
        PeekAndAdmin = NativeMethods.QUEUE_ACCESS_PEEK | NativeMethods.QUEUE_ACCESS_ADMIN,
        ReceiveAndAdmin = NativeMethods.QUEUE_ACCESS_RECEIVE | NativeMethods.QUEUE_ACCESS_ADMIN,

        ///
        /// SendAndReceive is supported for compatibility only.
        ///
        SendAndReceive = NativeMethods.QUEUE_ACCESS_SEND | NativeMethods.QUEUE_ACCESS_RECEIVE,

    }

    class QueueAccessModeHolder
    {
        readonly QueueAccessMode accessMode;

        static readonly Dictionary<QueueAccessMode, QueueAccessModeHolder> holders = new();

        QueueAccessModeHolder(QueueAccessMode accessMode)
        {
            this.accessMode = accessMode;
        }

        /// <devdoc>
        ///    <para>
        ///       Factory method for getting a QueueAccessModeHolder holder. For each accessMode, we want only one holder.
        ///    </para>
        /// </devdoc>
        public static QueueAccessModeHolder GetQueueAccessModeHolder(QueueAccessMode accessMode)
        {
            if (holders.TryGetValue(accessMode, out QueueAccessModeHolder value))
            {
                return value;
            }

            lock (holders)
            {
                QueueAccessModeHolder newHolder = new(accessMode);
                holders[accessMode] = newHolder;
                return newHolder;
            }

        }

        public bool CanRead()
        {
            return ((accessMode & QueueAccessMode.Receive) != 0) || ((accessMode & QueueAccessMode.Peek) != 0);
        }

        public bool CanWrite()
        {
            return (accessMode & QueueAccessMode.Send) != 0;
        }

        public int GetReadAccessMode()
        {
            int result = (int)(accessMode & ~QueueAccessMode.Send);
            if (result != 0)
            {
                return result;
            }
            // this is fail-fast path, when we know right away that the operation is incompatible with access mode
            // AccessDenied can also happen in other cases,
            // (for example, when we try to receive on a queue opened only for peek.
            // We'll let MQReceiveMessage enforce these rules
            throw new MessageQueueException((int)MessageQueueErrorCode.AccessDenied);
        }

        public int GetWriteAccessMode()
        {
            int result = (int)(accessMode & QueueAccessMode.Send);
            if (result != 0)
            {
                return result;
            }
            // this is fail-fast path, when we know right away that the operation is incompatible with access mode
            // AccessDenied can also happen in other cases,
            // (for example, when we try to receive on a queue opened only for peek.
            // We'll let MQReceiveMessage enforce these rules
            throw new MessageQueueException((int)MessageQueueErrorCode.AccessDenied);
        }
    }
}
