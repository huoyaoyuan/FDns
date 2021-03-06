namespace Meowtrix.FDns
{
    // https://datatracker.ietf.org/doc/html/rfc1035#section-3.2.4

    public enum DnsEndpointClass : short
    {
        /// <summary>
        /// The Internet
        /// </summary>
        IN = 1,
        /// <summary>
        /// The CHAOS class
        /// </summary>
        CH = 3,
        /// <summary>
        /// Hesiod
        /// </summary>
        HS = 4,

        /// <summary>
        /// Querying all classes
        /// </summary>
        /// <remarks>
        /// This value is only valid in a query.
        /// </remarks>
        QueryAll = 255,
    }
}
