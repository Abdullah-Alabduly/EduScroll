namespace EduScroll.Application.Exceptions;

/// <summary>
/// Base exception for expected application-level errors, distinguishing them from unexpected server failures.
/// </summary>
public class AppException : Exception
{
    public AppException(AppErrorKind kind, string errorCode, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Kind = kind;
        ErrorCode = errorCode;
    }

    public AppException(AppErrorKind kind, string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Kind = kind;
        ErrorCode = errorCode;
    }

    public AppErrorKind Kind { get; }

    public string ErrorCode { get; }
}
