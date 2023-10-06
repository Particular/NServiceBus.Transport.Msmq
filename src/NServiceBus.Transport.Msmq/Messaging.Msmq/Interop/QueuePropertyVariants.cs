//------------------------------------------------------------------------------
// <copyright file="QueuePropertyVariants.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Messaging.Msmq.Interop
{
    class QueuePropertyVariants : MessagePropertyVariants
    {

        const int MaxQueuePropertyIndex = 26;

        public QueuePropertyVariants()
            : base(MaxQueuePropertyIndex, NativeMethods.QUEUE_PROPID_BASE + 1)
        {
        }
    }
}
