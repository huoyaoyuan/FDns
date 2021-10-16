using System;
using System.Collections.Generic;
using System.Net.Sockets;
using Meowtrix.FDns.Records;
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
            Assert.False(message.IsResponse);
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

        [Fact]
        public void IPv4Answer()
        {
            byte[] packet = new byte[]
            {
                0, 0, 0x80, 0, 0, 0, 0, 1, 0, 0, 0, 0,
                7, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e',
                3, (byte)'c', (byte)'o', (byte)'m', 0,
                0, 1, 0, 1, 0, 0, 0x12, 0x34, 0, 4,
                93, 184, 216, 34
            };
            var message = DnsParser.ParseMessage(packet, out int bytesConsumed);
            Assert.Equal(packet.Length, bytesConsumed);

            Assert.True(message.IsResponse);
            Assert.Equal(1, message.Answers.Count);
            Assert.Equal(DomainType.A, message.Answers[0].Type);
            Assert.Equal(DnsEndpointClass.IN, message.Answers[0].EndpointClass);
            Assert.Equal(0x1234, message.Answers[0].AliveSeconds);
            var record = Assert.IsType<IPRecord>(message.Answers[0]);
            Assert.Equal(AddressFamily.InterNetwork, record.Address.AddressFamily);
            Assert.Equal("93.184.216.34", record.Address.ToString());
        }

        [Fact]
        public void IPv6Answer()
        {
            byte[] packet = new byte[]
            {
                0, 0, 0x80, 0, 0, 0, 0, 1, 0, 0, 0, 0,
                7, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e',
                3, (byte)'c', (byte)'o', (byte)'m', 0,
                0, 28, 0, 1, 0, 0, 0x12, 0x34, 0, 16,
                0x26, 0x06, 0x28, 0x00, 0x02, 0x20, 0x00, 0x01,
                0x02, 0x48, 0x18, 0x93, 0x25, 0xc8, 0x19, 0x46,
            };
            var message = DnsParser.ParseMessage(packet, out int bytesConsumed);
            Assert.Equal(packet.Length, bytesConsumed);

            Assert.True(message.IsResponse);
            Assert.Equal(1, message.Answers.Count);
            Assert.Equal(DomainType.AAAA, message.Answers[0].Type);
            Assert.Equal(DnsEndpointClass.IN, message.Answers[0].EndpointClass);
            Assert.Equal(0x1234, message.Answers[0].AliveSeconds);
            var record = Assert.IsType<IPRecord>(message.Answers[0]);
            Assert.Equal(AddressFamily.InterNetworkV6, record.Address.AddressFamily);
            Assert.Equal("2606:2800:220:1:248:1893:25c8:1946", record.Address.ToString());
        }

        [Fact]
        public void TxtAnswer()
        {
            byte[] packet = new byte[]
            {
                0, 0, 0x80, 0, 0, 0, 0, 1, 0, 0, 0, 0,
                7, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e',
                3, (byte)'c', (byte)'o', (byte)'m', 0,
                0, 16, 0, 1, 0, 0, 0x12, 0x34, 0, 13,
                (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o', (byte)',', (byte)' ',
                (byte)'w', (byte)'o', (byte)'r', (byte)'l', (byte)'d', (byte)'!',
            };
            var message = DnsParser.ParseMessage(packet, out int bytesConsumed);
            Assert.Equal(packet.Length, bytesConsumed);

            Assert.True(message.IsResponse);
            Assert.Equal(1, message.Answers.Count);
            Assert.Equal(DomainType.TXT, message.Answers[0].Type);
            Assert.Equal(DnsEndpointClass.IN, message.Answers[0].EndpointClass);
            Assert.Equal(0x1234, message.Answers[0].AliveSeconds);
            var record = Assert.IsType<TxtRecord>(message.Answers[0]);
            Assert.Equal("Hello, world!", record.Text);
        }
    }
}
