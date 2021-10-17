using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Meowtrix.FDns.Records;

namespace Meowtrix.FDns
{
    public static class DnsParser
    {
        public static ReadOnlySpan<byte> IDNAPrefix
            => new byte[] { (byte)'x', (byte)'n', (byte)'-', (byte)'-' };

        private const ushort ResponseFlag = 0b_1000_0000_0000_0000;
        private const ushort OpCodeMask = 0b_0111_1000_0000_0000;
        private const int OpCodeShift = 11;
        private const ushort AuthoritativeMask = 0b_0000_0100_0000_0000;
        private const ushort TruncationMask = 0b_0000_0010_0000_0000;
        private const ushort RecursionDesiredMask = 0b_0000_0001_0000_0000;
        private const ushort RecursionAvailableMask = 0b_0000_0000_1000_0000;
        private const ushort ResponseCodeMask = 0b_0000_0000_0000_1111;

        public static DnsMessage ParseMessage(ReadOnlySpan<byte> span, out int bytesConsumed)
        {
            var context = new DnsParseContext(span);

            var message = new DnsMessage
            {
                QueryId = context.ReadUInt16(),
            };
            ushort flags = context.ReadUInt16();
            int queryCount = context.ReadUInt16();
            int answerCount = context.ReadUInt16();
            int serverCount = context.ReadUInt16();
            int additionalCount = context.ReadUInt16();

            message.IsResponse = (flags & ResponseFlag) != 0;
            message.Operation = (DnsOperation)((flags & OpCodeMask) >> OpCodeShift);
            message.IsAuthoritativeAnswer = (flags & AuthoritativeMask) != 0;
            message.IsTruncated = (flags & TruncationMask) != 0;
            message.IsRecursionDesired = (flags & RecursionDesiredMask) != 0;
            message.IsRecursionAvailable = (flags & RecursionAvailableMask) != 0;
            message.ResponseCode = (DnsResponseCode)(flags & ResponseCodeMask);

            static IReadOnlyList<DnsResourceRecord>? ParseSection(ref DnsParseContext context, int count)
            {
                if (count == 0)
                    return null;

                var result = new DnsResourceRecord[count];
                foreach (ref var record in result.AsSpan())
                {
                    string name = context.ReadDomainName();
                    DomainType qtype = (DomainType)context.ReadUInt16();
                    DnsEndpointClass qclass = (DnsEndpointClass)context.ReadUInt16();
                    int ttl = context.ReadInt32();
                    ushort rdlength = context.ReadUInt16();

                    record = qtype switch
                    {
                        DomainType.A or DomainType.AAAA => new IPRecord(),
                        DomainType.TXT => new TxtRecord(),
                        _ => new UnknownRecord()
                    };
                    record.Name = name;
                    record.Type = qtype;
                    record.EndpointClass = qclass;
                    record.AliveSeconds = ttl;
                    record.ReadData(context.AvailableSpan[..rdlength]);
                    context.BytesConsumed += rdlength;
                }

                return result;
            }

            var queries = queryCount == 0 ? Array.Empty<DnsQuery>() : new DnsQuery[queryCount];
            foreach (ref var query in queries.AsSpan())
            {
                string name = context.ReadDomainName();
                DomainType qtype = (DomainType)context.ReadUInt16();
                DnsEndpointClass qclass = (DnsEndpointClass)context.ReadUInt16();
                query = new(name, qtype, qclass);
            }

            message.Queries = queries;
            message.Answers = ParseSection(ref context, answerCount);
            message.NameServerAuthorities = ParseSection(ref context, serverCount);
            message.AdditionalRecords = ParseSection(ref context, additionalCount);

            bytesConsumed = context.BytesConsumed;
            return message;
        }

        public static int FormatMessage(DnsMessage message, Span<byte> destination, bool enableNameCompression = false)
        {
            List<(int Index, string String)>? savedString = null;
            if (enableNameCompression)
                savedString = new();

            int FormatName(ReadOnlySpan<char> name, Span<byte> destination, int startIndex)
            {
                int totalBytesWritten = 0;
                while (!name.IsEmpty)
                {
                    if (savedString != null)
                    {
                        // Compression
                        foreach (var (index, saved) in savedString)
                        {
                            if (name.SequenceEqual(saved))
                            {
                                Debug.Assert(index <= 0b_0011_1111);

                                destination[0] = checked((byte)(0b_1100_0000 | index));
                                return totalBytesWritten + 1;
                            }
                        }

                        // Save for compression
                        int current = startIndex + totalBytesWritten;
                        if (current <= 0b_0011_1111)
                        {
                            savedString.Add((current, name.ToString()));
                        }
                    }

                    int delimiterIndex = name.IndexOf('.');
                    ReadOnlySpan<char> section;
                    if (delimiterIndex != -1)
                    {
                        section = name[..delimiterIndex];
                        name = name[(delimiterIndex + 1)..];
                    }
                    else
                    {
                        section = name;
                        name = default;
                    }

                    if (section.IsEmpty)
                        throw new FormatException("Invalid domain name.");

                    bool isAscii = true;
                    foreach (char c in section)
                    {
                        if (c >= 0x80)
                        {
                            isAscii = false;
                            break;
                        }
                    }

                    if (isAscii)
                    {
                        int bytesWritten = Encoding.ASCII.GetBytes(section, destination[1..]);
                        destination[0] = checked((byte)bytesWritten);
                        destination = destination[(bytesWritten + 1)..];
                        totalBytesWritten += bytesWritten + 1;
                    }
                    else
                    {
                        IDNAPrefix.CopyTo(destination[1..]);
                        int bytesWritten = PunyCode.EncodeToAscii(section, destination[(IDNAPrefix.Length + 1)..]);
                        destination[0] = checked((byte)(bytesWritten + IDNAPrefix.Length));
                        destination = destination[(bytesWritten + IDNAPrefix.Length + 1)..];
                        totalBytesWritten += bytesWritten + IDNAPrefix.Length + 1;
                    }
                }

                destination[0] = 0;
                return totalBytesWritten + 1;
            }

            BinaryPrimitives.WriteUInt16BigEndian(destination, message.QueryId);

            ushort flags = 0;
            if (message.IsResponse)
                flags |= ResponseFlag;
            flags |= (ushort)((ushort)message.Operation << OpCodeShift);
            if (message.IsAuthoritativeAnswer)
                flags |= AuthoritativeMask;
            //if (message.IsTruncated)
            //    flags |= TruncationMask;
            if (message.IsRecursionDesired)
                flags |= RecursionDesiredMask;
            if (message.IsRecursionAvailable)
                flags |= RecursionAvailableMask;
            flags |= (ushort)message.ResponseCode;
            BinaryPrimitives.WriteUInt16BigEndian(destination[2..], flags);
            BinaryPrimitives.WriteUInt16BigEndian(destination[4..], checked((ushort)message.Queries.Count));
            BinaryPrimitives.WriteUInt16BigEndian(destination[6..], checked((ushort)(message.Answers?.Count ?? 0)));
            BinaryPrimitives.WriteUInt16BigEndian(destination[8..], checked((ushort)(message.NameServerAuthorities?.Count ?? 0)));
            BinaryPrimitives.WriteUInt16BigEndian(destination[10..], checked((ushort)(message.AdditionalRecords?.Count ?? 0)));
            int bytesWritten = 12;

            int FormatSection(IReadOnlyList<DnsResourceRecord>? secton, Span<byte> destination, int startIndex)
            {
                if (secton is null)
                    return 0;

                int bytesWritten = 0;

                foreach (var record in secton)
                {
                    bytesWritten += FormatName(
                        record.Name ?? throw new InvalidOperationException("RR must have name"),
                        destination[bytesWritten..],
                        startIndex + bytesWritten);
                    BinaryPrimitives.WriteUInt16BigEndian(destination[bytesWritten..], (ushort)record.Type);
                    BinaryPrimitives.WriteUInt16BigEndian(destination[(bytesWritten + 2)..], (ushort)record.EndpointClass);
                    BinaryPrimitives.WriteInt32BigEndian(destination[(bytesWritten + 4)..], record.AliveSeconds);
                    int dataLength = record.WriteData(destination[(bytesWritten + 10)..]);
                    BinaryPrimitives.WriteUInt16BigEndian(destination[(bytesWritten + 8)..], checked((ushort)dataLength));
                    bytesWritten += dataLength + 10;
                }

                return bytesWritten;
            }

            foreach (var query in message.Queries)
            {
                bytesWritten += FormatName(query.QueryName, destination[bytesWritten..], bytesWritten);
                BinaryPrimitives.WriteUInt16BigEndian(destination[bytesWritten..], (ushort)query.QueryType);
                BinaryPrimitives.WriteUInt16BigEndian(destination[(bytesWritten + 2)..], (ushort)query.QueryClass);
                bytesWritten += 4;
            }

            bytesWritten += FormatSection(message.Answers, destination[bytesWritten..], bytesWritten);
            bytesWritten += FormatSection(message.NameServerAuthorities, destination[bytesWritten..], bytesWritten);
            bytesWritten += FormatSection(message.AdditionalRecords, destination[bytesWritten..], bytesWritten);
            return bytesWritten;
        }

        internal ref struct DnsParseContext
        {
            private readonly ReadOnlySpan<byte> _span;
            public int BytesConsumed { get; set; }
            public ReadOnlySpan<byte> AvailableSpan => _span[BytesConsumed..];

            public DnsParseContext(ReadOnlySpan<byte> span)
            {
                _span = span;
                BytesConsumed = 0;
            }

            public ushort ReadUInt16()
            {
                ushort result = BinaryPrimitives.ReadUInt16BigEndian(AvailableSpan);
                BytesConsumed += 2;
                return result;
            }

            public int ReadInt32()
            {
                int result = BinaryPrimitives.ReadInt32BigEndian(AvailableSpan);
                BytesConsumed += 4;
                return result;
            }

            public string ReadDomainName()
            {
                int position = BytesConsumed;
                Span<char> buffer = stackalloc char[256];
                int bufferIndex = 0;
                int bytesConsumed = 0;
                bool jumped = false;

                while (true)
                {
                    byte length = _span[position];
                    if ((length & 0b_1100_0000) != 0)
                    {
                        position = length & 0b_0011_1111;
                        jumped = true;
                        bytesConsumed++;
                        continue;
                    }

                    if (!jumped)
                        bytesConsumed += length + 1;

                    if (length == 0)
                        break;

                    if (bufferIndex > 0)
                        buffer[bufferIndex++] = '.';

                    var nameSpan = _span.Slice(position + 1, length);
                    int charsWritten = nameSpan.StartsWith(IDNAPrefix)
                        ? PunyCode.DecodeFromAscii(nameSpan[IDNAPrefix.Length..], buffer[bufferIndex..])
                        : Encoding.ASCII.GetChars(nameSpan, buffer[bufferIndex..]);
                    bufferIndex += charsWritten;
                    position = position + 1 + length;
                }

                BytesConsumed += bytesConsumed;
                return new string(buffer[..bufferIndex]);
            }
        }
    }
}
