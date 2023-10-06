//------------------------------------------------------------------------------
// <copyright file="IPersistStreamInit.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Messaging.Msmq.Interop
{
    using System;
    using System.Runtime.InteropServices;

    [ComImport(),
    Guid("7FD52380-4E07-101B-AE2D-08002B2EC713"),
    InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPersistStreamInit
    {
        void GetClassID([Out] out Guid pClassID);

        int IsDirty();

        void Load([In, MarshalAs(UnmanagedType.Interface)] IStream pstm);

        void Save([In, MarshalAs(UnmanagedType.Interface)] IStream pstm,
                  [In, MarshalAs(UnmanagedType.Bool)] bool fClearDirty);

        long GetSizeMax();

        void InitNew();
    }
}
