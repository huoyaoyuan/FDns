using System;

namespace Meowtrix.FDns.Records
{
    public class UnknownRecord : NormalRecord
    {
        public ReadOnlyMemory<byte> Data { get; set; }

        public override void ReadData(ReadOnlySpan<byte> data) => Data = data.ToArray();

        public override int WriteData(Span<byte> destination)
        {
            Data.Span.CopyTo(destination);
            return Data.Length;
        }
    }
}
