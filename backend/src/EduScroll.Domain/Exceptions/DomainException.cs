namespace EduScroll.Domain.Exceptions;

/// <summary>
/// Base exception for domain invariant or business-rule violations.
/// Transport-agnostic; mapped to client-safe Problem Details at the API boundary.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string errorCode, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        ErrorCode = errorCode;
    }

    protected DomainException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
