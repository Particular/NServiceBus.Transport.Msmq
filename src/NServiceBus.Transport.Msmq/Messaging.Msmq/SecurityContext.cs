//------------------------------------------------------------------------------
// <copyright file="UnsafeNativeMethods.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Messaging.Msmq
{
    using System;
    using Messaging.Msmq.Interop;

    public sealed class SecurityContext : IDisposable
    {
        readonly SecurityContextHandle handle;
        bool disposed;

        internal SecurityContext(SecurityContextHandle securityContext)
        {
            handle = securityContext;
        }

        internal SecurityContextHandle Handle
        {
            get
            {
                ObjectDisposedException.ThrowIf(disposed, GetType().Name);

                return handle;
            }
        }

        public void Dispose()
        {
            handle.Close();
            disposed = true;
        }
    }
}
