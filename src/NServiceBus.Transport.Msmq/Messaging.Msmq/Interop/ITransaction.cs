//------------------------------------------------------------------------------
// <copyright file="ITransaction.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Messaging.Msmq.Interop
{
    using System;
    using System.Runtime.InteropServices;
    using UnmanagedType = System.Runtime.InteropServices.UnmanagedType;

    [ComImport, Guid("0FB15084-AF41-11CE-BD2B-204C4F4F5020"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ITransaction
    {

        [return: MarshalAs(UnmanagedType.I4)]
        [PreserveSig]
        int Commit(
           [In, MarshalAs(UnmanagedType.I4)]
             int fRetaining,
           [In, MarshalAs(UnmanagedType.U4)]
             int grfTC,
           [In, MarshalAs(UnmanagedType.U4)]
             int grfRM);

        [return: MarshalAs(UnmanagedType.I4)]
        [PreserveSig]
        int Abort(
           [In, MarshalAs(UnmanagedType.U4)]
              int pboidReason,
           [In, MarshalAs(UnmanagedType.I4)]
             int fRetaining,
           [In, MarshalAs(UnmanagedType.I4)]
             int fAsync);

        [return: MarshalAs(UnmanagedType.I4)]
        [PreserveSig]
        int GetTransactionInfo(
           [In, Out]
            IntPtr /* XACTTRANSINFO */ pinfo);
    }
}
