//------------------------------------------------------------------------------
// <copyright file="Columns.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Messaging.Msmq.Interop
{
    using System;
    using System.Globalization; //for CultureInfo
    using System.Runtime.InteropServices;

    class Columns
    {
        readonly int maxCount;
        readonly MQCOLUMNSET columnSet = new();

        public Columns(int maxCount)
        {
            this.maxCount = maxCount;
            columnSet.columnIdentifiers = Marshal.AllocHGlobal(maxCount * 4);
            columnSet.columnCount = 0;
        }

        public virtual void AddColumnId(int columnId)
        {
            lock (this)
            {
                if (columnSet.columnCount >= maxCount)
                {
                    throw new InvalidOperationException(Res.GetString(Res.TooManyColumns, maxCount.ToString(CultureInfo.CurrentCulture)));
                }

                ++columnSet.columnCount;
                columnSet.SetId(columnId, columnSet.columnCount - 1);
            }
        }

        public virtual MQCOLUMNSET GetColumnsRef()
        {
            return columnSet;
        }

        [StructLayout(LayoutKind.Sequential)]
        public class MQCOLUMNSET
        {
            public int columnCount;

            public IntPtr columnIdentifiers;

            ~MQCOLUMNSET()
            {
                if (columnIdentifiers != 0)
                {
                    Marshal.FreeHGlobal(columnIdentifiers);
                    columnIdentifiers = 0;
                }
            }

            public virtual void SetId(int columnId, int index)
            {
                Marshal.WriteInt32(checked((IntPtr)((long)columnIdentifiers + (index * 4))), columnId);
            }
        }
    }
}
