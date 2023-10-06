//------------------------------------------------------------------------------
// <copyright file="IPersistStream.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Messaging.Msmq.Interop
{
    using System;
    using System.Runtime.InteropServices;

    [ComImport(),
    Guid("00000109-0000-0000-C000-000000000046"),
    InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPersistStream
    {
        void GetClassID([Out] out Guid pClassID);

        int IsDirty();

        void Load([In, MarshalAs(UnmanagedType.Interface)] IStream pstm);

        void Save([In, MarshalAs(UnmanagedType.Interface)] IStream pstm,
                  [In, MarshalAs(UnmanagedType.Bool)] bool fClearDirty);

        long GetSizeMax();
    }
}
