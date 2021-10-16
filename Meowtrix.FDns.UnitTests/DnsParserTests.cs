using System;
using System.Collections.Generic;
using Xunit;

namespace Meowtrix.FDns.UnitTests
{
    public class DnsParserTests
    {
        [Fact]
        public void EmptyMessage()
        {
            byte[] packet = new byte[]
            {
                0x12, 0x34, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            };
            var message = DnsParser.ParseMessage(packet, out int bytesConsumed);
            Assert.Equal(packet.Length, bytesConsumed);

            Assert.Equal(0x1234, message.QueryId);
            Assert.Empty(message.Queries);
            Assert.Null(message.Answers);
            Assert.Null(message.NameServerAuthorities);
            Assert.Null(message.AdditionalRecords);
        }

        [Fact]
        public void Flags()
        {
            byte[] packet = new byte[]
            {
                0, 0, 0b_0000_1010, 0b1000_0101, 0, 0, 0, 0, 0, 0, 0, 0,
            };
            var message = DnsParser.ParseMessage(packet, out int bytesConsumed);
            Assert.Equal(packet.Length, bytesConsumed);
            Assert.False(message.IsQuery);
            Assert.Equal(DnsOperation.InverseQuery, message.Operation);
            Assert.False(message.IsAuthoritativeAnswer);
            Assert.True(message.IsTruncated);
            Assert.False(message.IsRecursionDesired);
            Assert.True(message.IsRecursionAvailable);
            Assert.Equal(DnsResponseCode.Refused, message.ResponseCode);
        }

        public static IEnumerable<object[]> IncompletePacketData
        {
            get
            {
                yield return new object[] { Array.Empty<byte>() };
                yield return new object[] { new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 } };
                yield return new object[] { new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 } };
            }
        }

        [Theory]
        [MemberData(nameof(IncompletePacketData))]
        public void IncompletePakcet(byte[] packet)
        {
            Assert.ThrowsAny<Exception>(() => DnsParser.ParseMessage(packet, out _));
        }

        [Fact]
        public void Query()
        {
            byte[] packet = new byte[]
            {
                0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0,
                1, (byte)'a', 2, (byte)'b', (byte) 'c', 0,
                0, 1, 0, 1,
            };
            var message = DnsParser.ParseMessage(packet, out int bytesConsumed);
            Assert.Equal(packet.Length, bytesConsumed);

            Assert.Equal(1, message.Queries.Count);
            Assert.Equal("a.bc", message.Queries[0].QueryName);
            Assert.Equal(DomainType.A, message.Queries[0].QueryType);
            Assert.Equal(DnsEndpointClass.IN, message.Queries[0].QueryClass);
        }

        [Fact]
        public void NamePointer()
        {
            byte[] packet = new byte[]
            {
                0, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0,
                1, (byte)'a', 2, (byte)'b', (byte) 'c', 0,
                0, 1, 0, 1,
                0b_1100_0000 + 14,
                0, 1, 0, 1,
                7, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e',
                3, (byte)'c', (byte)'o', (byte)'m', 0,
                0, 1, 0, 1,
            };
            var message = DnsParser.ParseMessage(packet, out int bytesConsumed);
            Assert.Equal(packet.Length, bytesConsumed);

            Assert.Equal(3, message.Queries.Count);
            Assert.Equal("a.bc", message.Queries[0].QueryName);
            Assert.Equal("bc", message.Queries[1].QueryName);
            Assert.Equal("example.com", message.Queries[2].QueryName);
        }

        [Fact]
        public void UnicodeDomainName()
        {
            byte[] packet = new byte[]
            {
                0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0,
                17, (byte)'x', (byte)'n', (byte) '-', (byte)'-', (byte)'r', (byte)'h', (byte)'q', (byte)'r', (byte)'3', (byte)'y',
                (byte)'k', (byte)'w', (byte)'b', (byte)'x', (byte)'v', (byte)'0', (byte)'c',
                3, (byte)'t', (byte)'o', (byte)'p', 0,
                0, 1, 0, 1,
            };
            var message = DnsParser.ParseMessage(packet, out int bytesConsumed);
            Assert.Equal(packet.Length, bytesConsumed);

            Assert.Equal(1, message.Queries.Count);
            Assert.Equal("世界大学.top", message.Queries[0].QueryName);
        }
    }
}
