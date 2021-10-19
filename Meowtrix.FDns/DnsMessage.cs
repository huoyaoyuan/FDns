using System;
using System.Collections.Generic;

namespace Meowtrix.FDns
{
    public record class DnsMessage
    {
        public ushort QueryId { get; set; }
        public bool IsResponse { get; set; }
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
        DnsRecordType QueryType,
        DnsEndpointClass QueryClass);

    public abstract record class DnsResourceRecord
    {
        public string? Name { get; set; }
        public DnsRecordType Type { get; set; }
        public DnsEndpointClass EndpointClass { get; set; }
        public int AliveSeconds { get; set; }

        internal abstract void ReadData(ref DnsParser.DnsParseContext context, int length);
        internal abstract int WriteData(ref DnsParser.DnsFormatContext context);
    }
}
