using System.Net;
using System.Net.Sockets;
using EduScroll.Application.Exceptions;
using EduScroll.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace EduScroll.Infrastructure.WebExtraction;

public interface IWebHttpTransport
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}

/// <summary>
/// Hardened outbound HTTP transport that disables proxies, cookies, credentials, and auto-redirects,
/// and validates resolved IP addresses directly inside the socket connect callback to mitigate DNS rebinding.
/// </summary>
public sealed class SecureSocketsHttpTransport : IWebHttpTransport, IDisposable
{
    private readonly HttpClient _httpClient;

    public SecureSocketsHttpTransport(
        IDnsResolver dnsResolver,
        IOptions<SourceIngestionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dnsResolver);
        ArgumentNullException.ThrowIfNull(options);

        var webFetch = options.Value.WebFetch;

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            Credentials = null,
            PreAuthenticate = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(webFetch.ConnectTimeoutSeconds),
            MaxResponseHeadersLength = 16,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = async (context, cancellationToken) =>
            {
                var syntheticUri = new UriBuilder(Uri.UriSchemeHttp, context.DnsEndPoint.Host, context.DnsEndPoint.Port).Uri;
                var validatedAddresses = await SsrfUrlGuard.ValidateResolvedAddressesAsync(
                    syntheticUri,
                    dnsResolver,
                    isRedirect: false,
                    cancellationToken);

                foreach (var ip in validatedAddresses)
                {
                    var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                    {
                        NoDelay = true
                    };

                    try
                    {
                        await socket.ConnectAsync(ip, context.DnsEndPoint.Port, cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (OperationCanceledException)
                    {
                        socket.Dispose();
                        throw;
                    }
                    catch (Exception)
                    {
                        socket.Dispose();
                    }
                }

                throw new ExternalDependencyAppException(
                    "Unable to establish a connection to the target web server.",
                    "URL_FETCH_FAILED");
            }
        };

        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan // Enforced per-operation via linked CancellationTokenSource
        };
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
