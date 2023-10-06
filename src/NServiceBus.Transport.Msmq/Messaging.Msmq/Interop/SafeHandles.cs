//------------------------------------------------------------------------------
// <copyright file="IPersistStreamInit.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Messaging.Msmq.Interop
{
    using System;
    using Microsoft.Win32.SafeHandles;

    class MessageQueueHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public static readonly MessageQueueHandle InvalidHandle = new InvalidMessageQueueHandle();

        protected MessageQueueHandle() : base(true) { }

        protected override bool ReleaseHandle()
        {
            var result = SafeNativeMethods.MQCloseQueue(handle);

            if (result != NativeMethods.MQ_OK)
            {
                throw new MessageQueueException(result);
            }

            return true;
        }

        public override bool IsInvalid => base.IsInvalid || IsClosed;

        // A subclass needed to express InvalidHandle. The reason is that CLR notices that
        // ReleaseHandle requires a call to MQRT.DLL, and throws in the ctor if MQRT.DLL is not available,
        // even though CTOR ITSELF DOES NOT REQUIRE MQRT.DLL.
        // We address this by defining a NOOP ReleaseHandle
        sealed class InvalidMessageQueueHandle : MessageQueueHandle
        {
            protected override bool ReleaseHandle()
            {
                return true;
            }
        }
    }

    class CursorHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public static readonly CursorHandle NullHandle = new InvalidCursorHandle();

        protected CursorHandle() : base(true) { }

        protected override bool ReleaseHandle()
        {
            var result = SafeNativeMethods.MQCloseCursor(handle);

            if (result != NativeMethods.MQ_OK)
            {
                throw new MessageQueueException(result);
            }

            return true;
        }

        public override bool IsInvalid => base.IsInvalid || IsClosed;

        // A subclass needed to express InvalidHandle. The reason is that CLR notices that
        // ReleaseHandle requires a call to MQRT.DLL, and throws in the ctor if MQRT.DLL is not available,
        // even though CTOR ITSELF DOES NOT REQUIRE MQRT.DLL.
        // We address this by defining a NOOP ReleaseHandle
        sealed class InvalidCursorHandle : CursorHandle
        {
            protected override bool ReleaseHandle()
            {
                return true;
            }
        }
    }

    class LocatorHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public static readonly LocatorHandle InvalidHandle = new InvalidLocatorHandle();

        protected LocatorHandle() : base(true) { }

        protected override bool ReleaseHandle()
        {
            var result = SafeNativeMethods.MQLocateEnd(handle);

            if (result != NativeMethods.MQ_OK)
            {
                throw new MessageQueueException(result);
            }

            return true;
        }

        public override bool IsInvalid => base.IsInvalid || IsClosed;

        // A subclass needed to express InvalidHandle. The reason is that CLR notices that
        // ReleaseHandle requires a call to MQRT.DLL, and throws in the ctor if MQRT.DLL is not available,
        // even though CTOR ITSELF DOES NOT REQUIRE MQRT.DLL.
        // We address this by defining a NOOP ReleaseHandle
        sealed class InvalidLocatorHandle : LocatorHandle
        {
            protected override bool ReleaseHandle()
            {
                return true;
            }
        }
    }

    sealed class SecurityContextHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal SecurityContextHandle(IntPtr existingHandle)
            : base(true)
        {
            SetHandle(existingHandle);
        }

        protected override bool ReleaseHandle()
        {
            SafeNativeMethods.MQFreeSecurityContext(handle);

            return true;
        }

        public override bool IsInvalid => base.IsInvalid || IsClosed;
    }
}

