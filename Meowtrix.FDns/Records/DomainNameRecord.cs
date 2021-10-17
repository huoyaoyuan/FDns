using System;

namespace Meowtrix.FDns.Records
{
    public class DomainNameRecord : DnsResourceRecord
    {
        public string? TargetDomainName { get; set; }

        internal override void ReadData(ref DnsParser.DnsParseContext context, int length)
        {
            int original = context.BytesConsumed;
            TargetDomainName = context.ReadDomainName();
            if (context.BytesConsumed - original != length)
                throw new InvalidOperationException("RR data length overrun.");
        }

        internal override int WriteData(ref DnsParser.DnsFormatContext context)
        {
            int original = context.BytesWritten;
            context.WriteDomainName(TargetDomainName);
            return context.BytesWritten - original;
        }
    }
}
