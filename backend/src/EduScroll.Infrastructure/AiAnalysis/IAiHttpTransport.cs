namespace EduScroll.Infrastructure.AiAnalysis;

public interface IAiHttpTransport
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}

public sealed class OllamaHttpTransport : IAiHttpTransport, IDisposable
{
    private readonly HttpClient _httpClient;

    public OllamaHttpTransport()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };

        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan // Per-attempt timeout enforced via linked CancellationTokenSource
        };
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
