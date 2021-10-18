using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Meowtrix.FDns
{
    public class TcpDnsClient
    {
        public const int DefaultPort = 53;

        private readonly IPEndPoint _server;

        public TcpDnsClient(IPAddress serverAddress)
            : this(new IPEndPoint(serverAddress, DefaultPort))
        {
        }

        public TcpDnsClient(IPEndPoint serverEndPoint) => _server = serverEndPoint;

        public async ValueTask<DnsMessage> QueryAsync(DnsMessage queryMessage, CancellationToken cancellationToken = default)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(2048);
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(_server).ConfigureAwait(false);
                using var stream = new NetworkStream(socket, true);

                int bytesWritten = DnsParser.FormatMessage(queryMessage, buffer.AsSpan(2), true);
                BinaryPrimitives.WriteUInt16BigEndian(buffer, checked((ushort)bytesWritten));
                await stream.WriteAsync(buffer.AsMemory(0, bytesWritten + 2), cancellationToken).ConfigureAwait(false);

                await ReadToFillAsync(stream, buffer.AsMemory(0, 2), cancellationToken);
                int bytesToReceive = BinaryPrimitives.ReadUInt16BigEndian(buffer);
                await ReadToFillAsync(stream, buffer.AsMemory(0, bytesToReceive), cancellationToken).ConfigureAwait(false);

                return DnsParser.ParseMessage(buffer.AsSpan(0, bytesToReceive), out _);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static async ValueTask ReadToFillAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            while (!buffer.IsEmpty)
            {
                int bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                    throw new EndOfStreamException();
                buffer = buffer[bytesRead..];
            }
        }
    }
}
