using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Meowtrix.FDns;
using Meowtrix.FDns.Records;

Func<DnsMessage, ValueTask<DnsMessage>> queryMethod;

Console.Write("Choose DNS method: [T]CP / [U]DP / HTTP [G]ET / HTTP [P]OST: ");
switch (Console.ReadLine()![0])
{
    case 'G' or 'g':
    {
        Console.WriteLine("Using HTTP GET.");

        Uri? serverUri;
        while (true)
        {
            Console.Write("Server url: ");
            if (Uri.TryCreate(Console.ReadLine(), UriKind.Absolute, out serverUri)
                && serverUri.Scheme is "http" or "https")
                break;
            Console.WriteLine("Can't parse server url.");
        }
        var client = new HttpsDnsClient(serverUri);
        queryMethod = m => client.QueryAsync(m, HttpMethod.Get);
        break;
    }
    case 'P' or 'p':
    {
        Console.WriteLine("Using HTTP POST.");

        Uri? serverUri;
        while (true)
        {
            Console.Write("Server url: ");
            if (Uri.TryCreate(Console.ReadLine(), UriKind.Absolute, out serverUri)
                && serverUri.Scheme is "http" or "https")
                break;
            Console.WriteLine("Can't parse server url.");
        }
        var client = new HttpsDnsClient(serverUri);
        queryMethod = m => client.QueryAsync(m, HttpMethod.Post);
        break;
    }
    case 'U' or 'u':
    {
        Console.WriteLine("Using UDP.");

        IPAddress? serverAddress;
        while (true)
        {
            Console.Write("DNS Server ip: ");
            if (IPAddress.TryParse(Console.ReadLine(), out serverAddress))
                break;
            Console.WriteLine("Can't parse ip.");
        }
        var client = new UdpDnsClient(serverAddress);
        queryMethod = m => client.QueryAsync(m);
        break;
    }
    default:
    {
        Console.WriteLine("Using TCP.");

        IPAddress? serverAddress;
        while (true)
        {
            Console.Write("DNS Server ip: ");
            if (IPAddress.TryParse(Console.ReadLine(), out serverAddress))
                break;
            Console.WriteLine("Can't parse ip.");
        }
        var client = new TcpDnsClient(serverAddress);
        queryMethod = m => client.QueryAsync(m);
        break;
    }
}

while (true)
{
    Console.Write("> ");
    string domainName = Console.ReadLine()!;
    var message = new DnsMessage
    {
        Queries = new[]
        {
            new DnsQuery(domainName, DnsRecordType.QueryAll, DnsEndpointClass.IN)
        }
    };

    try
    {
        var response = await queryMethod(message);

        Console.WriteLine($"Response code: {response.ResponseCode}");
        if (response.Answers is null or { Count: 0 })
        {
            Console.WriteLine("The server does not return any response");
        }
        else
        {
            foreach (var answer in response.Answers)
            {
                Console.WriteLine($"Domain: {answer.Name}");
                Console.WriteLine($"Type: {answer.Type}");
                Console.WriteLine($"TTL: {TimeSpan.FromSeconds(answer.AliveSeconds)} ({answer.AliveSeconds}s)");

                Console.WriteLine(answer switch
                {
                    IPRecord ip => $"Address: {ip.Address}",
                    DomainNameRecord { Type: DnsRecordType.CNAME } cname => $"Alias of: {cname.TargetDomainName}",
                    MXRecord mx => $"Preference: {mx.PreferenceOrder}, Mail server: {mx.MailServerDomainName}",
                    TxtRecord txt => $"Text data: {txt.Text}",
                    SoaRecord soa => $"SOA Zone: {soa.ZoneName}",
                    _ => "Unknown data"
                });

                Console.WriteLine();
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
    }
}
