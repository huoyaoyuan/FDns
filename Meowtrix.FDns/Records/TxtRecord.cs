using System;
using System.Text;

namespace Meowtrix.FDns.Records
{
    public record class TxtRecord : NormalRecord
    {
        public string? Text { get; set; }

        public override void ReadData(ReadOnlySpan<byte> data)
            => Text = Encoding.UTF8.GetString(data);
        public override int WriteData(Span<byte> destination)
            => Encoding.UTF8.GetBytes(Text, destination);
    }
}
