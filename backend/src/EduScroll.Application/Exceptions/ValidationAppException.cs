namespace EduScroll.Application.Exceptions;

public sealed class ValidationAppException : AppException
{
    public ValidationAppException(
        string message,
        IReadOnlyDictionary<string, string[]> errors,
        string errorCode = "VALIDATION_FAILED")
        : base(AppErrorKind.Validation, errorCode, message)
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
