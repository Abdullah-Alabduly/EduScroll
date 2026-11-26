using System.Diagnostics;
using EduScroll.API.Middleware;
using EduScroll.Application.Exceptions;
using EduScroll.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EduScroll.API.ErrorHandling;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var correlationId = httpContext.Items[CorrelationIdMiddleware.ItemKey] as string
            ?? httpContext.TraceIdentifier;

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation(
                "Request aborted by client. CorrelationId={CorrelationId} Path={Path}",
                correlationId,
                httpContext.Request.Path.Value);

            httpContext.Response.StatusCode = 499;
            return true;
        }

        var problemDetails = BuildProblemDetails(httpContext, exception, correlationId);
        var statusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;

        LogException(exception, statusCode, correlationId, httpContext.Request.Path.Value);

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }

    private ProblemDetails BuildProblemDetails(
        HttpContext httpContext,
        Exception exception,
        string correlationId)
    {
        ProblemDetails details = exception switch
        {
            ValidationAppException validationEx => CreateValidationProblem(validationEx),
            DomainException domainEx => new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Domain rule violation.",
                Detail = domainEx.Message,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.21",
                Extensions = { ["code"] = domainEx.ErrorCode }
            },
            AppException appEx => CreateAppProblem(appEx),
            _ => CreateUnexpectedProblem(exception)
        };

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        details.Instance = httpContext.Request.Path.Value;
        details.Extensions["traceId"] = traceId;
        details.Extensions["correlationId"] = correlationId;

        return details;
    }

    private static ValidationProblemDetails CreateValidationProblem(ValidationAppException validationEx)
    {
        var errors = validationEx.Errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        var details = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Detail = validationEx.Message,
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
        };
        details.Extensions["code"] = validationEx.ErrorCode;
        return details;
    }

    private static ProblemDetails CreateAppProblem(AppException appEx)
    {
        var (status, title, type) = appEx.Kind switch
        {
            AppErrorKind.Validation => (StatusCodes.Status400BadRequest, "Validation error.", "https://tools.ietf.org/html/rfc9110#section-15.5.1"),
            AppErrorKind.Unauthorized => (StatusCodes.Status401Unauthorized, "Authentication failed.", "https://tools.ietf.org/html/rfc9110#section-15.5.2"),
            AppErrorKind.NotFound => (StatusCodes.Status404NotFound, "Resource not found.", "https://tools.ietf.org/html/rfc9110#section-15.5.5"),
            AppErrorKind.Conflict => (StatusCodes.Status409Conflict, "Resource conflict.", "https://tools.ietf.org/html/rfc9110#section-15.5.10"),
            AppErrorKind.Forbidden => (StatusCodes.Status403Forbidden, "Access forbidden.", "https://tools.ietf.org/html/rfc9110#section-15.5.4"),
            AppErrorKind.PayloadTooLarge => (StatusCodes.Status413PayloadTooLarge, "Payload too large.", "https://tools.ietf.org/html/rfc9110#section-15.5.14"),
            AppErrorKind.UnprocessableEntity => (StatusCodes.Status422UnprocessableEntity, "Unable to process content.", "https://tools.ietf.org/html/rfc9110#section-15.5.21"),
            AppErrorKind.ExternalDependency => (StatusCodes.Status502BadGateway, "Upstream dependency failure.", "https://tools.ietf.org/html/rfc9110#section-15.6.3"),
            AppErrorKind.Timeout => (StatusCodes.Status504GatewayTimeout, "Upstream request timed out.", "https://tools.ietf.org/html/rfc9110#section-15.6.5"),
            _ => (StatusCodes.Status400BadRequest, "Application error.", "https://tools.ietf.org/html/rfc9110#section-15.5.1")
        };

        return new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = appEx.Message,
            Type = type,
            Extensions = { ["code"] = appEx.ErrorCode }
        };
    }

    private ProblemDetails CreateUnexpectedProblem(Exception exception)
    {
        var details = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = environment.IsDevelopment()
                ? exception.Message
                : "An unexpected internal error occurred. Reference the traceId when contacting support.",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            Extensions = { ["code"] = "INTERNAL_SERVER_ERROR" }
        };

        if (environment.IsDevelopment())
        {
            details.Extensions["exceptionType"] = exception.GetType().FullName;
        }

        return details;
    }

    private void LogException(Exception exception, int statusCode, string correlationId, string? path)
    {
        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled exception on {Path} (Status={StatusCode}, CorrelationId={CorrelationId})",
                path,
                statusCode,
                correlationId);
        }
        else
        {
            logger.LogWarning(
                "Handled application/domain exception on {Path}: {ExceptionType} (Status={StatusCode}, CorrelationId={CorrelationId})",
                path,
                exception.GetType().Name,
                statusCode,
                correlationId);
        }
    }
}
