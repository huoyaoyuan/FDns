using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Meowtrix.FDns.Records;

namespace Meowtrix.FDns
{
    internal static class DnsParser
    {
        public static ReadOnlySpan<byte> IDNAPrefix
            => new byte[] { (byte)'x', (byte)'n', (byte)'-', (byte)'-' };

        private const ushort QueryFlag = 0b_1000_0000_0000_0000;
        private const ushort OpCodeMask = 0b_0111_1000_0000_0000;
        private const int OpCodeShift = 11;
        private const ushort AuthoritativeMask = 0b_0000_0100_0000_0000;
        private const ushort TruncationMask = 0b_0000_0010_0000_0000;
        private const ushort RecursionDesiredMask = 0b_0000_0001_0000_0000;
        private const ushort RecursionAvailableMask = 0b_0000_0000_1000_0000;
        private const ushort ResponseCodeMask = 0b_0000_0000_0000_1111;

        public static DnsMessage ParseMessage(ReadOnlySpan<byte> span, out int bytesConsumed)
        {
            static string ParseName(ReadOnlySpan<byte> span, int position, out int bytesConsumed)
            {
                Span<char> buffer = stackalloc char[256];
                int bufferIndex = 0;
                bytesConsumed = 0;
                bool jumped = false;

                while (true)
                {
                    byte length = span[position];
                    if ((length & 0b_1100_0000) != 0)
                    {
                        position = length & 0b_0011_1111;
                        jumped = true;
                        continue;
                    }

                    if (!jumped)
                        bytesConsumed += length + 1;

                    if (length == 0)
                        break;

                    if (bufferIndex > 0)
                        buffer[bufferIndex++] = '.';

                    var nameSpan = span.Slice(position + 1, length);
                    int charsWritten = nameSpan.StartsWith(IDNAPrefix)
                        ? PunyCode.DecodeFromAscii(nameSpan[IDNAPrefix.Length..], buffer[bufferIndex..])
                        : Encoding.ASCII.GetChars(nameSpan, buffer[bufferIndex..]);
                    bufferIndex += charsWritten;
                    position = position + 1 + length;
                }

                return new string(buffer);
            }

            bytesConsumed = 12;
            var message = new DnsMessage
            {
                QueryId = BinaryPrimitives.ReadInt16BigEndian(span)
            };
            ushort flags = BinaryPrimitives.ReadUInt16BigEndian(span[2..]);
            int queryCount = BinaryPrimitives.ReadUInt16BigEndian(span[4..]);
            int answerCount = BinaryPrimitives.ReadUInt16BigEndian(span[6..]);
            int serverCount = BinaryPrimitives.ReadUInt16BigEndian(span[8..]);
            int additionalCount = BinaryPrimitives.ReadUInt16BigEndian(span[10..]);

            message.IsQuery = (flags & QueryFlag) != 0;
            message.Operation = (DnsOperation)((flags & OpCodeMask) >> OpCodeShift);
            message.IsAuthoritativeAnswer = (flags & AuthoritativeMask) != 0;
            message.IsTruncated = (flags & TruncationMask) != 0;
            message.IsRecursionDesired = (flags & RecursionDesiredMask) != 0;
            message.IsRecursionAvailable = (flags & RecursionAvailableMask) != 0;
            message.ResponseCode = (DnsResponseCode)(flags & ResponseCodeMask);

            static IReadOnlyList<DnsResourceRecord>? ParseSection(ReadOnlySpan<byte> span, int count, ref int bytesConsumed)
            {
                if (count == 0)
                    return null;

                var result = new DnsResourceRecord[count];
                foreach (ref var record in result.AsSpan())
                {
                    string name = ParseName(span, bytesConsumed, out int nameConsumed);
                    bytesConsumed += nameConsumed;
                    DomainType qtype = (DomainType)BinaryPrimitives.ReadUInt16BigEndian(span[bytesConsumed..]);
                    DnsEndpointClass qclass = (DnsEndpointClass)BinaryPrimitives.ReadUInt16BigEndian(span[(bytesConsumed + 2)..]);
                    int ttl = BinaryPrimitives.ReadInt32BigEndian(span[(bytesConsumed + 4)..]);
                    ushort rdlength = BinaryPrimitives.ReadUInt16BigEndian(span[(bytesConsumed + 8)..]);
                    bytesConsumed += 10;

                    record = qtype switch
                    {
                        _ => new UnknownRecord()
                    };
                    record.Name = name;
                    record.Type = qtype;
                    record.EndpointClass = qclass;
                    record.AliveSeconds = ttl;
                    record.ReadData(span.Slice(bytesConsumed, rdlength));
                    bytesConsumed += rdlength;
                }

                return result;
            }

            var queries = queryCount == 0 ? Array.Empty<DnsQuery>() : new DnsQuery[queryCount];
            foreach (ref var query in queries.AsSpan())
            {
                string name = ParseName(span, bytesConsumed, out int nameConsumed);
                bytesConsumed += nameConsumed;
                DomainType qtype = (DomainType)BinaryPrimitives.ReadUInt16BigEndian(span[bytesConsumed..]);
                DnsEndpointClass qclass = (DnsEndpointClass)BinaryPrimitives.ReadUInt16BigEndian(span[(bytesConsumed + 2)..]);
                bytesConsumed += 4;
                query = new(name, qtype, qclass);
            }

            message.Queries = queries;
            message.Answers = ParseSection(span, answerCount, ref bytesConsumed);
            message.NameServerAuthorities = ParseSection(span, serverCount, ref bytesConsumed);
            message.AdditionalRecords = ParseSection(span, additionalCount, ref bytesConsumed);
            return message;
        }

        public static int FormatMessage(DnsMessage message, Span<byte> destination)
        {
            static int FormatName(ReadOnlySpan<char> name, Span<byte> destination)
            {
                int totalBytesWritten = 0;
                while (!name.IsEmpty)
                {
                    int delimiterIndex = name.IndexOf('.');
                    ReadOnlySpan<char> section;
                    if (delimiterIndex == -1)
                    {
                        section = name[delimiterIndex..];
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
                        int bytesWritten = Encoding.ASCII.GetBytes(section, destination);
                        destination = destination[bytesWritten..];
                        totalBytesWritten += bytesWritten;
                    }
                    else
                    {
                        IDNAPrefix.CopyTo(destination);
                        destination = destination[IDNAPrefix.Length..];
                        int bytesWritten = PunyCode.EncodeToAscii(section, destination);
                        destination = destination[bytesWritten..];
                        totalBytesWritten += bytesWritten + IDNAPrefix.Length;
                    }
                }

                destination[0] = 0;
                return totalBytesWritten + 1;
            }

            BinaryPrimitives.WriteInt16BigEndian(destination, message.QueryId);

            ushort flags = 0;
            if (message.IsQuery)
                flags |= QueryFlag;
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

            static int FormatSection(IReadOnlyList<DnsResourceRecord>? secton, Span<byte> destination)
            {
                if (secton is null)
                    return 0;

                int bytesWritten = 0;

                foreach (var record in secton)
                {
                    bytesWritten += FormatName(
                        record.Name ?? throw new InvalidOperationException("RR must have name"),
                        destination[bytesWritten..]);
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
                bytesWritten += FormatName(query.QueryName, destination[bytesWritten..]);
                BinaryPrimitives.WriteUInt16BigEndian(destination[bytesWritten..], (ushort)query.QueryType);
                BinaryPrimitives.WriteUInt16BigEndian(destination[(bytesWritten + 2)..], (ushort)query.QueryClass);
                bytesWritten += 4;
            }

            bytesWritten += FormatSection(message.Answers, destination[bytesWritten..]);
            bytesWritten += FormatSection(message.NameServerAuthorities, destination[bytesWritten..]);
            bytesWritten += FormatSection(message.AdditionalRecords, destination[bytesWritten..]);
            return bytesWritten;
        }
    }
}
