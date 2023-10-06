//------------------------------------------------------------------------------
// <copyright file="MessageQueueException.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Messaging.Msmq
{
    using System;
    using System.Globalization;
    using System.Runtime.InteropServices;
    using System.Text;
    using Messaging.Msmq.Interop;

    /// <include file='doc\MessageQueueException.uex' path='docs/doc[@for="MessageQueueException"]/*' />
    /// <devdoc>
    ///    <para>
    ///       Is thrown if a Microsoft Message
    ///       Queue Server (MSMQ) internal error occurs.
    ///    </para>
    /// </devdoc>
    public class MessageQueueException : ExternalException
    {
        readonly int nativeErrorCode;

        /// <include file='doc\MessageQueueException.uex' path='docs/doc[@for="MessageQueueException.MessageQueueException"]/*' />
        /// <internalonly/>
        internal MessageQueueException(int error)
        {
            nativeErrorCode = error;
        }

        /// <include file='doc\MessageQueueException.uex' path='docs/doc[@for="MessageQueueException.MessageQueueErrorCode"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public MessageQueueErrorCode MessageQueueErrorCode => (MessageQueueErrorCode)nativeErrorCode;

        /// <include file='doc\MessageQueueException.uex' path='docs/doc[@for="MessageQueueException.Message"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public override string Message
        {
            get
            {
                try
                {
                    return Res.GetString(Convert.ToString(nativeErrorCode, 16).ToUpper(CultureInfo.InvariantCulture));
                }
                catch
                {
                    return GetUnknownErrorMessage(nativeErrorCode);
                }
            }
        }

        static string GetUnknownErrorMessage(int error)
        {
            StringBuilder sb = new(256);
            int result = SafeNativeMethods.FormatMessage(SafeNativeMethods.FORMAT_MESSAGE_IGNORE_INSERTS |
                                       SafeNativeMethods.FORMAT_MESSAGE_FROM_SYSTEM |
                                       SafeNativeMethods.FORMAT_MESSAGE_ARGUMENT_ARRAY,
                                       IntPtr.Zero, error, 0, sb, sb.Capacity + 1, IntPtr.Zero);
            //get the system error message...
            string errorMsg;
            if (result != 0)
            {
                int i = sb.Length;
                while (i > 0)
                {
                    char ch = sb[i - 1];
                    if (ch is > (char)32 and not '.')
                    {
                        break;
                    }

                    i--;
                }
                errorMsg = sb.ToString(0, i);
            }
            else
            {
                errorMsg = Res.GetString(Res.UnknownError, Convert.ToString(error, 16));
            }

            return errorMsg;
        }
    }
}
