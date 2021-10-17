namespace Meowtrix.FDns.Records
{
    public class DomainNameRecord : DnsResourceRecord
    {
        public string? TargetDomainName { get; set; }

        internal override void ReadData(ref DnsParser.DnsParseContext context, int length)
            => TargetDomainName = context.ReadDomainName();

        internal override int WriteData(ref DnsParser.DnsFormatContext context)
        {
            int original = context.BytesWritten;
            context.WriteDomainName(TargetDomainName);
            return context.BytesWritten - original;
        }
    }
}
