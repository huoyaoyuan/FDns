using System;
using System.Net;
using Meowtrix.FDns;
using Meowtrix.FDns.Records;

IPAddress? serverAddress;

while (true)
{
    Console.Write("DNS Server ip:");
    if (IPAddress.TryParse(Console.ReadLine(), out serverAddress))
        break;
    Console.WriteLine("Can't parse ip.");
}

var client = new TcpDnsClient(serverAddress);

while (true)
{
    Console.Write("> ");
    string domainName = Console.ReadLine()!;
    var message = new DnsMessage
    {
        Queries = new[]
        {
            new DnsQuery(domainName, (DomainType)255, DnsEndpointClass.IN)
        }
    };

    try
    {
        var response = await client.QueryAsync(message);
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

                Console.WriteLine(answer switch
                {
                    IPRecord ip => $"Address: {ip}",
                    DomainNameRecord { Type: DomainType.CNAME } cname => $"Alias of: {cname.TargetDomainName}",
                    TxtRecord txt => $"Text data: {txt.Text}",
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
