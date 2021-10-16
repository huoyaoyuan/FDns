using System;
using System.Net;

namespace Meowtrix.FDns.Records
{
    public class IPRecord : DnsResourceRecord
    {
        public IPAddress? Address { get; set; }

        public override void ReadData(ReadOnlySpan<byte> data) => Address = new IPAddress(data);

        public override int WriteData(Span<byte> destination)
        {
            if (Address is null)
                throw new InvalidOperationException("Address is not set.");

            if (Address.TryWriteBytes(destination, out int bytesWritten))
                return bytesWritten;
            else
                throw new ArgumentException("Destination too small", nameof(destination));
        }
    }
}
