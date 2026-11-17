using System.Diagnostics;

namespace EduScroll.API.Middleware;

public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";
    private const int MaxCorrelationIdLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = ResolveOrCreateCorrelationId(context.Request.Headers);
        context.Items[ItemKey] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["TraceId"] = traceId
        });

        var startTimestamp = Stopwatch.GetTimestamp();
        await next(context);
        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        var statusCode = context.Response.StatusCode;
        var path = context.Request.Path.Value ?? "/";

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:0.0} ms",
                context.Request.Method,
                path,
                statusCode,
                elapsedMs);
        }
        else if (path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase) &&
                 statusCode < StatusCodes.Status400BadRequest)
        {
            logger.LogDebug(
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:0.0} ms",
                context.Request.Method,
                path,
                statusCode,
                elapsedMs);
        }
        else
        {
            logger.LogInformation(
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:0.0} ms",
                context.Request.Method,
                path,
                statusCode,
                elapsedMs);
        }
    }

    private static string ResolveOrCreateCorrelationId(IHeaderDictionary headers)
    {
        if (headers.TryGetValue(HeaderName, out var values))
        {
            var candidate = values.ToString().Trim();
            if (IsValidCorrelationId(candidate))
            {
                return candidate;
            }
        }

        return Guid.NewGuid().ToString("N");
    }

    private static bool IsValidCorrelationId(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxCorrelationIdLength)
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not ('-' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
