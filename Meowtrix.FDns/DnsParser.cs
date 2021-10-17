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
                        DomainType.CNAME => new DomainNameRecord(),
                        _ => new UnknownRecord()
                    };
                    record.Name = name;
                    record.Type = qtype;
                    record.EndpointClass = qclass;
                    record.AliveSeconds = ttl;

                    record.ReadData(ref context, rdlength);
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
            var context = new DnsFormatContext(destination, enableNameCompression);

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

            context.WriteUInt16(message.QueryId);
            context.WriteUInt16(flags);
            context.WriteUInt16(checked((ushort)message.Queries.Count));
            context.WriteUInt16(checked((ushort)(message.Answers?.Count ?? 0)));
            context.WriteUInt16(checked((ushort)(message.NameServerAuthorities?.Count ?? 0)));
            context.WriteUInt16(checked((ushort)(message.AdditionalRecords?.Count ?? 0)));

            static void FormatSection(IReadOnlyList<DnsResourceRecord>? secton, ref DnsFormatContext context)
            {
                if (secton is null)
                    return;

                foreach (var record in secton)
                {
                    context.WriteDomainName(record.Name ?? throw new InvalidOperationException("RR must have name"));
                    context.WriteUInt16((ushort)record.Type);
                    context.WriteUInt16((ushort)record.EndpointClass);
                    context.WriteInt32(record.AliveSeconds);
                    var lengthSpan = context.AvailableSpan;
                    context.BytesWritten += 2;
                    int dataLength = record.WriteData(ref context);
                    BinaryPrimitives.WriteUInt16BigEndian(lengthSpan, checked((ushort)dataLength));
                }
            }

            foreach (var query in message.Queries)
            {
                context.WriteDomainName(query.QueryName);
                context.WriteUInt16((ushort)query.QueryType);
                context.WriteUInt16((ushort)query.QueryClass);
            }

            FormatSection(message.Answers, ref context);
            FormatSection(message.NameServerAuthorities, ref context);
            FormatSection(message.AdditionalRecords, ref context);
            return context.BytesWritten;
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

        internal ref struct DnsFormatContext
        {
            private readonly Span<byte> _span;
            public int BytesWritten { get; set; }
            public Span<byte> AvailableSpan => _span[BytesWritten..];
            private readonly List<(int Index, string String)>? _savedString;

            public DnsFormatContext(Span<byte> span, bool allowNameCompression)
            {
                _span = span;
                BytesWritten = 0;
                _savedString = allowNameCompression ? new() : null;
            }

            public void WriteUInt16(ushort value)
            {
                BinaryPrimitives.WriteUInt16BigEndian(AvailableSpan, value);
                BytesWritten += 2;
            }

            public void WriteInt32(int value)
            {
                BinaryPrimitives.WriteInt32BigEndian(AvailableSpan, value);
                BytesWritten += 4;
            }

            public void WriteDomainName(ReadOnlySpan<char> name)
            {
                while (!name.IsEmpty)
                {
                    if (_savedString != null)
                    {
                        // Compression
                        foreach (var (index, saved) in _savedString)
                        {
                            if (name.SequenceEqual(saved))
                            {
                                Debug.Assert(index <= 0b_0011_1111);

                                AvailableSpan[0] = checked((byte)(0b_1100_0000 | index));
                                BytesWritten++;
                                return;
                            }
                        }

                        // Save for compression
                        int current = BytesWritten;
                        if (current <= 0b_0011_1111)
                        {
                            _savedString.Add((current, name.ToString()));
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
                        int bytesWritten = Encoding.ASCII.GetBytes(section, AvailableSpan[1..]);
                        AvailableSpan[0] = checked((byte)bytesWritten);
                        BytesWritten += bytesWritten + 1;
                    }
                    else
                    {
                        IDNAPrefix.CopyTo(AvailableSpan[1..]);
                        int bytesWritten = PunyCode.EncodeToAscii(section, AvailableSpan[(IDNAPrefix.Length + 1)..]);
                        AvailableSpan[0] = checked((byte)(bytesWritten + IDNAPrefix.Length));
                        BytesWritten += bytesWritten + IDNAPrefix.Length + 1;
                    }
                }

                AvailableSpan[0] = 0;
                BytesWritten++;
            }
        }
    }
}
