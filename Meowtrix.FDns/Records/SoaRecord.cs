using System;

namespace Meowtrix.FDns.Records
{
    public class SoaRecord : DnsResourceRecord
    {
        public string? ZoneName { get; set; }
        public string? MailBoxName { get; set; }
        public uint VersionNumber { get; set; }
        public int RefreshInterval { get; set; }
        public int RetryInterval { get; set; }
        public int Expires { get; set; }
        public uint MinimumTTL { get; set; }

        internal override void ReadData(ref DnsParser.DnsParseContext context, int length)
        {
            int original = context.BytesConsumed;
            ZoneName = context.ReadDomainName();
            MailBoxName = context.ReadDomainName();
            VersionNumber = context.ReadUInt32();
            RefreshInterval = context.ReadInt32();
            RetryInterval = context.ReadInt32();
            Expires = context.ReadInt32();
            MinimumTTL = context.ReadUInt32();
            if (context.BytesConsumed - original != length)
                throw new InvalidOperationException("RR data length overrun.");
        }

        internal override int WriteData(ref DnsParser.DnsFormatContext context)
        {
            int original = context.BytesWritten;
            context.WriteDomainName(ZoneName);
            context.WriteDomainName(MailBoxName);
            context.WriteUInt32(VersionNumber);
            context.WriteInt32(RefreshInterval);
            context.WriteInt32(RetryInterval);
            context.WriteInt32(Expires);
            context.WriteUInt32(MinimumTTL);
            return context.BytesWritten - original;
        }
    }
}
