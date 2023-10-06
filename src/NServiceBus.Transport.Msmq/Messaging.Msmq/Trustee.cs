namespace Messaging.Msmq
{
    using System;
    using System.ComponentModel;

    /// <include file='doc\Trustee.uex' path='docs/doc[@for="Trustee"]/*' />
    /// <devdoc>
    ///    <para>[To be supplied.]</para>
    /// </devdoc>
    public class Trustee
    {
        string name;
        TrusteeType trusteeType;

        /// <include file='doc\Trustee.uex' path='docs/doc[@for="Trustee.Name"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public string Name
        {
            get { return name; }
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                name = value;
            }
        }

        /// <include file='doc\Trustee.uex' path='docs/doc[@for="Trustee.SystemName"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public string SystemName { get; set; }

        /// <include file='doc\Trustee.uex' path='docs/doc[@for="Trustee.TrusteeType"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public TrusteeType TrusteeType
        {
            get { return trusteeType; }
            set
            {
                if (!ValidationUtility.ValidateTrusteeType(value))
                {
                    throw new InvalidEnumArgumentException("value", (int)value, typeof(TrusteeType));
                }

                trusteeType = value;
            }
        }

        /// <include file='doc\Trustee.uex' path='docs/doc[@for="Trustee.Trustee"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public Trustee()
        {
        }

        /// <include file='doc\Trustee.uex' path='docs/doc[@for="Trustee.Trustee1"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public Trustee(string name) : this(name, null) { }

        /// <include file='doc\Trustee.uex' path='docs/doc[@for="Trustee.Trustee2"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public Trustee(string name, string systemName) : this(name, systemName, TrusteeType.Unknown) { }

        /// <include file='doc\Trustee.uex' path='docs/doc[@for="Trustee.Trustee3"]/*' />
        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public Trustee(string name, string systemName, TrusteeType trusteeType)
        {
            Name = name;
            SystemName = systemName;
            TrusteeType = trusteeType;
        }
    }
}
