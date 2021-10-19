namespace Meowtrix.FDns
{
    // https://datatracker.ietf.org/doc/html/rfc1035#section-3.2.2
    // https://datatracker.ietf.org/doc/html/rfc3596#section-2.1
    // List of types: https://en.wikipedia.org/wiki/List_of_DNS_record_types

    public enum DnsRecordType : short
    {
        /// <summary>
        /// host address
        /// </summary>
        A = 1,
        /// <summary>
        /// Authoritative name server
        /// </summary>
        NS = 2,
        /// <summary>
        /// Canonical name for an alias
        /// </summary>
        CNAME = 5,
        /// <summary>
        /// Start of a zone of authority
        /// </summary>
        SOA = 6,
        /// <summary>
        /// Mail exchange
        /// </summary>
        MX = 15,
        /// <summary>
        /// Text strings
        /// </summary>
        TXT = 16,
        /// <summary>
        /// IPv6 host address
        /// </summary>
        AAAA = 28,

        /// <summary>
        /// Querying all types
        /// </summary>
        /// <remarks>
        /// This value is only valid in a query.
        /// </remarks>
        QueryAll = 255,
    }
}
