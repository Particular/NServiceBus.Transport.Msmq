#pragma warning disable IDE1006 // Ignore naming rule for interop code
namespace NServiceBus.Transport.Msmq
{
    using System;
    using System.ComponentModel;
    using System.Runtime.InteropServices;
    using System.Security.Principal;
    using Particular.Msmq;

    /// <summary>
    /// Reads the Access Control Entries (ACE) from an MSMQ queue.
    /// </summary>
    /// <remarks>
    /// There is no managed API for reading the queue permissions, this has to be done via P/Invoke. by calling
    /// <c>MQGetQueueSecurity</c> API.
    /// See http://stackoverflow.com/questions/10177255/how-to-get-the-current-permissions-for-an-msmq-private-queue
    /// </remarks>
    static class MessageQueueExtensions
    {
        [DllImport("mqrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern int MQGetQueueSecurity(string formatName, int SecurityInformation, IntPtr SecurityDescriptor, int length, out int lengthNeeded);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetSecurityDescriptorDacl(IntPtr pSD, out bool daclPresent, out IntPtr pDacl, out bool daclDefaulted);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetAclInformation(IntPtr pAcl, ref ACL_SIZE_INFORMATION pAclInformation, uint nAclInformationLength, ACL_INFORMATION_CLASS dwAclInformationClass);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern int GetAce(IntPtr aclPtr, int aceIndex, out IntPtr acePtr);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern int GetLengthSid(IntPtr pSID);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool ConvertSidToStringSid([MarshalAs(UnmanagedType.LPArray)] byte[] pSID, out IntPtr ptrSid);

        const int DACL_SECURITY_INFORMATION = 4;
        const int MQ_ERROR_SECURITY_DESCRIPTOR_TOO_SMALL = unchecked((int)0xc00e0023);
        const int MQ_OK = 0;

        //Security constants

        // the following constants taken from MessageQueue.cs (see http://referencesource.microsoft.com/#System.Messaging/System/Messaging/MessageQueue.cs)
        [StructLayout(LayoutKind.Sequential)]
        struct ACE_HEADER
        {
            public byte AceType;
            public byte AceFlags;
            public short AceSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ACCESS_ALLOWED_ACE
        {
            public ACE_HEADER Header;
            public uint Mask;
            public int SidStart;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ACL_SIZE_INFORMATION
        {
            public uint AceCount;
            public uint AclBytesInUse;
            public uint AclBytesFree;
        }

        enum ACL_INFORMATION_CLASS
        {
            AclRevisionInformation = 1,
            AclSizeInformation
        }

        public static bool TryGetPermissions(this MessageQueue queue, string user, out MessageQueueAccessRights? rights, out AccessControlEntryType? accessType)
        {
            var sid = GetSidForUser(user);

            try
            {
                rights = GetPermissions(queue.FormatName, sid, out accessType);
                return true;
            }
            catch
            {
                rights = null;
                accessType = null;
                return false;
            }
        }

        static MessageQueueAccessRights GetPermissions(string formatName, string sid, out AccessControlEntryType? aceType)
        {
            var SecurityDescriptor = new byte[100];

            var sdHandle = GCHandle.Alloc(SecurityDescriptor, GCHandleType.Pinned);
            try
            {
                var mqResult = MQGetQueueSecurity(formatName,
                    DACL_SECURITY_INFORMATION,
                    sdHandle.AddrOfPinnedObject(),
                    SecurityDescriptor.Length,
                    out var lengthNeeded);

                if (mqResult == MQ_ERROR_SECURITY_DESCRIPTOR_TOO_SMALL)
                {
                    sdHandle.Free();
                    SecurityDescriptor = new byte[lengthNeeded];
                    sdHandle = GCHandle.Alloc(SecurityDescriptor, GCHandleType.Pinned);
                    mqResult = MQGetQueueSecurity(formatName,
                        DACL_SECURITY_INFORMATION,
                        sdHandle.AddrOfPinnedObject(),
                        SecurityDescriptor.Length,
                        out lengthNeeded);
                }

                if (mqResult != MQ_OK)
                {
                    throw new Exception($"Unable to read the security descriptor of queue [{formatName}]");
                }

                var success = GetSecurityDescriptorDacl(sdHandle.AddrOfPinnedObject(),
                    out _,
                    out var pDacl,
                    out _);

                if (!success)
                {
                    throw new Win32Exception();
                }

                var allowedAce = GetAce(pDacl, sid);

                // The ACE_HEADER information contains the access control information as to whether it is allowed or denied.
                // In Interop, this value is a byte and can be any of the values defined in here: https://msdn.microsoft.com/en-us/library/windows/desktop/aa374919(v=vs.85).aspx
                // If the value is 0, then it equates to Allow. If the value is 1, then it equates to Deny.
                // However, you can't cast it directly to the AccessControlEntryType enumeration, as a value of 1 in the enumeration is
                // defined to be Allow!! Hence a translation is required.
                aceType = allowedAce.Header.AceType switch
                {
                    0 => (AccessControlEntryType?)AccessControlEntryType.Allow,
                    1 => (AccessControlEntryType?)AccessControlEntryType.Deny,
                    _ => null,
                };
                return (MessageQueueAccessRights)allowedAce.Mask;
            }
            finally
            {
                if (sdHandle.IsAllocated)
                {
                    sdHandle.Free();
                }
            }
        }

        static string GetSidForUser(string username)
        {
            var account = new NTAccount(username);
            var sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));

            return sid.ToString();
        }

        static ACCESS_ALLOWED_ACE GetAce(IntPtr pDacl, string sid)
        {
            var AclSize = new ACL_SIZE_INFORMATION();
            GetAclInformation(pDacl, ref AclSize, (uint)Marshal.SizeOf(typeof(ACL_SIZE_INFORMATION)), ACL_INFORMATION_CLASS.AclSizeInformation);

            for (var i = 0; i < AclSize.AceCount; i++)
            {
                GetAce(pDacl, i, out var pAce);
                var ace = (ACCESS_ALLOWED_ACE)Marshal.PtrToStructure(pAce, typeof(ACCESS_ALLOWED_ACE));
                var iter = (IntPtr)(pAce + (long)Marshal.OffsetOf(typeof(ACCESS_ALLOWED_ACE), "SidStart"));
                var size = GetLengthSid(iter);
                var bSID = new byte[size];
                Marshal.Copy(iter, bSID, 0, size);
                ConvertSidToStringSid(bSID, out var ptrSid);

                var strSID = Marshal.PtrToStringAuto(ptrSid);

                if (strSID == sid)
                {
                    return ace;
                }
            }

            throw new Exception($"No ACE for SID {sid} found in security descriptor");
        }
    }
}