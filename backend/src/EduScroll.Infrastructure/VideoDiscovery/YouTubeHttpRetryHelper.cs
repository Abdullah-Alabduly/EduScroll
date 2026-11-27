using System.Net;

namespace EduScroll.Infrastructure.VideoDiscovery;

internal static class YouTubeHttpRetryHelper
{
    public static bool IsTransientStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    public static Task DelayBackoffAsync(
        int baseDelayMs,
        int attempt,
        CancellationToken cancellationToken)
    {
        if (baseDelayMs <= 0)
        {
            return Task.CompletedTask;
        }

        // Bounded exponential backoff: base * 2^(attempt-1), capped at 10s.
        var factor = 1 << Math.Min(attempt - 1, 4);
        var delayMs = (int)Math.Min((long)baseDelayMs * factor, 10_000L);
        return Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
    }
}
