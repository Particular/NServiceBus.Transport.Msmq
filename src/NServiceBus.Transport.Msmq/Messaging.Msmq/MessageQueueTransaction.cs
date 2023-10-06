//------------------------------------------------------------------------------
// <copyright file="MessageQueueTransaction.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Messaging.Msmq
{
    using System;
    using System.Threading;
    using Messaging.Msmq.Interop;

    /// <include file='doc\MessageQueueTransaction.uex' path='docs/doc[@for="MessageQueueTransaction"]/*' />
    /// <devdoc>
    ///    <para>[To be supplied.]</para>
    /// </devdoc>
    public class MessageQueueTransaction : IDisposable
    {
        ITransaction internalTransaction;
        bool disposed;

        /// <include file='doc\MessageQueueTransaction.uex' path='docs/doc[@for="MessageQueueTransaction.MessageQueueTransaction"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Creates a new Message Queuing internal transaction context.
        ///    </para>
        /// </devdoc>
        public MessageQueueTransaction()
        {
            Status = MessageQueueTransactionStatus.Initialized;
        }

        internal ITransaction InnerTransaction => internalTransaction;

        /// <include file='doc\MessageQueueTransaction.uex' path='docs/doc[@for="MessageQueueTransaction.Status"]/*' />
        /// <devdoc>
        ///    <para>
        ///       The status of the transaction that this object represents.
        ///    </para>
        /// </devdoc>
        public MessageQueueTransactionStatus Status { get; private set; }

        /// <include file='doc\MessageQueueTransaction.uex' path='docs/doc[@for="MessageQueueTransaction.Abort"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Rolls back the pending internal transaction.
        ///    </para>
        /// </devdoc>
        public void Abort()
        {
            lock (this)
            {
                if (internalTransaction == null)
                {
                    throw new InvalidOperationException(Res.GetString(Res.TransactionNotStarted));
                }
                else
                {
                    AbortInternalTransaction();
                }
            }
        }

        /// <include file='doc\MessageQueueTransaction.uex' path='docs/doc[@for="MessageQueueTransaction.AbortInternalTransaction"]/*' />
        /// <internalonly/>
        void AbortInternalTransaction()
        {
            int status = internalTransaction.Abort(0, 0, 0);
            if (MessageQueue.IsFatalError(status))
            {
                throw new MessageQueueException(status);
            }

            internalTransaction = null;
            Status = MessageQueueTransactionStatus.Aborted;
        }

        /// <include file='doc\MessageQueueTransaction.uex' path='docs/doc[@for="MessageQueueTransaction.Begin"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Begins a new Message Queuing internal transaction context.
        ///    </para>
        /// </devdoc>
        public void Begin()
        {
            //Won't allow begining a new transaction after the object has been disposed.
            ObjectDisposedException.ThrowIf(disposed, GetType().Name);

            lock (this)
            {
                if (internalTransaction != null)
                {
                    throw new InvalidOperationException(Res.GetString(Res.TransactionStarted));
                }
                else
                {
                    int status = SafeNativeMethods.MQBeginTransaction(out internalTransaction);
                    if (MessageQueue.IsFatalError(status))
                    {
                        internalTransaction = null;
                        throw new MessageQueueException(status);
                    }

                    Status = MessageQueueTransactionStatus.Pending;
                }
            }
        }

        /// <include file='doc\MessageQueueTransaction.uex' path='docs/doc[@for="MessageQueueTransaction.BeginQueueOperation"]/*' />
        /// <internalonly/>
        internal ITransaction BeginQueueOperation()
        {
#pragma warning disable 0618
            //@TODO: This overload of Monitor.Enter is obsolete.  Please change this to use Monitor.Enter(ref bool), and remove the pragmas   -- ericeil
            Monitor.Enter(this);
#pragma warning restore 0618
            return internalTransaction;
        }

        /// <include file='doc\MessageQueueTransaction.uex' path='docs/doc[@for="MessageQueueTransaction.Commit"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Commits a pending internal transaction.
        ///    </para>
        /// </devdoc>
        public void Commit()
        {
            lock (this)
            {
                if (internalTransaction == null)
                {
                    throw new InvalidOperationException(Res.GetString(Res.TransactionNotStarted));
                }
                else
                {
                    int status = internalTransaction.Commit(0, 0, 0);
                    if (MessageQueue.IsFatalError(status))
                    {
                        throw new MessageQueueException(status);
                    }

                    internalTransaction = null;
                    Status = MessageQueueTransactionStatus.Committed;
                }
            }
        }

        /// <include file='doc\MessageQueueTransaction.uex' path='docs/doc[@for="MessageQueueTransaction.Dispose"]/*' />
        /// <devdoc>
        ///    <para>
        ///       Disposes this transaction instance, if it is in a
        ///       pending status, the transaction will be aborted.
        ///    </para>
        /// </devdoc>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <include file='doc\MessageQueueTransaction.uex' path='docs/doc[@for="MessageQueueTransaction.Dispose1"]/*' />
        /// <devdoc>
        ///    <para>
        ///    </para>
        /// </devdoc>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (this)
                {
                    if (internalTransaction != null)
                    {
                        AbortInternalTransaction();
                    }
                }
            }

            disposed = true;
        }

        /// <include file='doc\MessageQueueTransaction.uex' path='docs/doc[@for="MessageQueueTransaction.Finalize"]/*' />
        /// <internalonly/>
        ~MessageQueueTransaction()
        {
            Dispose(false);
        }

        /// <include file='doc\MessageQueueTransaction.uex' path='docs/doc[@for="MessageQueueTransaction.EndQueueOperation"]/*' />
        /// <internalonly/>
        internal void EndQueueOperation()
        {
            Monitor.Exit(this);
        }
    }
}
