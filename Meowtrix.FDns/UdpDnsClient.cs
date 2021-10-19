using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Meowtrix.FDns
{
    public class UdpDnsClient
    {
        public const int DefaultPort = 53;

        private readonly IPEndPoint _server;

        private static readonly IPEndPoint s_localAny = new(IPAddress.Any, 0);

        public UdpDnsClient(IPAddress serverAddress)
            : this(new IPEndPoint(serverAddress, DefaultPort))
        {
        }

        public UdpDnsClient(IPEndPoint serverEndPoint) => _server = serverEndPoint;

        public async ValueTask<DnsMessage> QueryAsync(DnsMessage queryMessage, CancellationToken cancellationToken = default)
        {
            using var socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(s_localAny);

            byte[]? buffer = ArrayPool<byte>.Shared.Rent(4096);
            try
            {
                int bytesWritten = DnsParser.FormatMessage(queryMessage, buffer, true);
                int bytesSent = await socket.SendToAsync(new ArraySegment<byte>(buffer, 0, bytesWritten), SocketFlags.None, _server);
                if (bytesWritten != bytesSent)
                    throw new InvalidOperationException("");

                cancellationToken.ThrowIfCancellationRequested();

                var receiveResult = await socket.ReceiveFromAsync(buffer, SocketFlags.None, _server);
                return DnsParser.ParseMessage(buffer.AsSpan(0, receiveResult.ReceivedBytes), out _);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
