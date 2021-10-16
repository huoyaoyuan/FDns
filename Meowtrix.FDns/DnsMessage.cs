using System;
using System.Collections.Generic;

namespace Meowtrix.FDns
{
    public record class DnsMessage
    {
        public short QueryId { get; set; }
        public bool IsQuery { get; set; }
        public DnsOperation Operation { get; set; }
        public bool IsAuthoritativeAnswer { get; set; }
        public bool IsTruncated { get; set; }
        public bool IsRecursionDesired { get; set; }
        public bool IsRecursionAvailable { get; set; }
        public DnsResponseCode ResponseCode { get; set; }

        public IReadOnlyList<DnsQuery> Queries { get; set; } = Array.Empty<DnsQuery>();
        public IReadOnlyList<DnsResourceRecord>? Answers { get; set; }
        public IReadOnlyList<DnsResourceRecord>? NameServerAuthorities { get; set; }
        public IReadOnlyList<DnsResourceRecord>? AdditionalRecords { get; set; }
    }

    public enum DnsOperation
    {
        Query = 0,
        InverseQuery = 1,
        Status = 2,
    }

    public enum DnsResponseCode
    {
        Success = 0,
        FormatError = 1,
        ServerFailure = 2,
        NameError = 3,
        NotImplemented = 4,
        Refused = 5,
    }

    public record class DnsQuery(
        string QueryName,
        DomainType QueryType,
        DnsEndpointClass QueryClass);

    public abstract class DnsResourceRecord
    {
        public string? Name { get; set; }
        public DomainType Type { get; set; }
        public DnsEndpointClass EndpointClass { get; set; }
        public int AliveSeconds { get; set; }

        public abstract void ReadData(ReadOnlySpan<byte> data);
        public abstract int WriteData(Span<byte> destination);
    }
}
