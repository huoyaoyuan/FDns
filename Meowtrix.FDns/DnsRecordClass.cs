namespace Meowtrix.FDns
{
    // https://datatracker.ietf.org/doc/html/rfc1035#section-3.2.4

    public enum DnsRecordClass : short
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
    }
}
