//------------------------------------------------------------------------------
// <copyright file="IPersist.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Messaging.Msmq.Interop
{
    using System;
    using System.Runtime.InteropServices;

    [ComImport(),
    Guid("0000010C-0000-0000-C000-000000000046"),
    InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPersist
    {
        void GetClassID([Out] out Guid pClassID);
    }
}
