using System;

namespace Meowtrix.FDns.Records
{
    public abstract class NormalRecord : DnsResourceRecord
    {
        internal sealed override void ReadData(ref DnsParser.DnsParseContext context, int length)
        {
            ReadData(context.AvailableSpan[..length]);
            context.BytesConsumed += length;
        }

        internal override int WriteData(ref DnsParser.DnsFormatContext context)
        {
            int length = WriteData(context.AvailableSpan);
            context.BytesWritten += length;
            return length;
        }

        public abstract void ReadData(ReadOnlySpan<byte> data);
        public abstract int WriteData(Span<byte> destination);
    }
}
