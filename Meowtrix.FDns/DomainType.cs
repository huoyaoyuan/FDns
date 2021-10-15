namespace Meowtrix.FDns
{
    // https://datatracker.ietf.org/doc/html/rfc1035#section-3.2.2
    // https://datatracker.ietf.org/doc/html/rfc3596#section-2.1

    public enum DomainType : short
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
    }
}
