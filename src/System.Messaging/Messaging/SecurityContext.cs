//------------------------------------------------------------------------------
// <copyright file="UnsafeNativeMethods.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>                                                                
//------------------------------------------------------------------------------

using System.Messaging.Interop;
using System;

namespace System.Messaging
{
    public sealed class SecurityContext : IDisposable
    {

        SecurityContextHandle handle;
        bool disposed;

        internal SecurityContext(SecurityContextHandle securityContext)
        {
            handle = securityContext;
        }

        internal SecurityContextHandle Handle
        {
            get
            {
                if (disposed)
                    throw new ObjectDisposedException(GetType().Name);

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
