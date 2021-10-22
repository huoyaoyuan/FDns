using System;
using System.Buffers;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Meowtrix.FDns
{
    public sealed class HttpsDnsClient : IDisposable
    {
        private readonly Uri _baseUri;
        private readonly HttpClient _httpClient = new();

        private const string MediaType = "application/dns-message";
        private static readonly MediaTypeHeaderValue s_mediaType = new(MediaType);
        private static readonly MediaTypeWithQualityHeaderValue s_accepts = new(MediaType);

        public HttpsDnsClient(Uri baseUri) => _baseUri = baseUri;

        public void Dispose() => _httpClient.Dispose();

        public async ValueTask<DnsMessage> QueryAsync(DnsMessage queryMessage, HttpMethod method, CancellationToken cancellationToken = default)
        {
            if (method != HttpMethod.Get && method != HttpMethod.Post)
            {
                throw new ArgumentException("Method must be GET or POST.", nameof(method));
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);
            try
            {
                int bytesWritten = DnsParser.FormatMessage(queryMessage, buffer, true);

                HttpRequestMessage request;
                if (method == HttpMethod.Get)
                {
                    string urlSafeQuery = Convert.ToBase64String(buffer.AsSpan(0, bytesWritten))
                        .TrimEnd('=')
                        .Replace('+', '-')
                        .Replace('/', '_');
                    request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUri}?dns={urlSafeQuery}");
                }
                else
                {
                    request = new HttpRequestMessage(HttpMethod.Post, _baseUri)
                    {
                        Content = new ByteArrayContent(buffer, 0, bytesWritten)
                        {
                            Headers =
                            {
                                ContentType = s_mediaType
                            }
                        }
                    };
                }

                request.Headers.Accept.Add(s_accepts);

                using (request)
                {
                    using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    byte[]? responseBuffer = await response.EnsureSuccessStatusCode().Content
#if NET5_0_OR_GREATER
                        .ReadAsByteArrayAsync(cancellationToken)
#else
                        .ReadAsByteArrayAsync()
#endif
                        .ConfigureAwait(false);
                    return DnsParser.ParseMessage(responseBuffer, out _);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
