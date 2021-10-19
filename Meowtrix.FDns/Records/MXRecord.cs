using System;

namespace Meowtrix.FDns.Records
{
    public class MXRecord : DnsResourceRecord
    {
        public int PreferenceOrder { get; set; }
        public string? MailServerDomainName { get; set; }

        internal override void ReadData(ref DnsParser.DnsParseContext context, int length)
        {
            int original = context.BytesConsumed;
            PreferenceOrder = context.ReadInt16();
            MailServerDomainName = context.ReadDomainName();
            if (context.BytesConsumed - original != length)
                throw new InvalidOperationException("RR data length overrun.");
        }

        internal override int WriteData(ref DnsParser.DnsFormatContext context)
        {
            int original = context.BytesWritten;
            context.WriteInt16(checked((short)PreferenceOrder));
            context.WriteDomainName(MailServerDomainName);
            return context.BytesWritten - original;
        }
    }
}
