namespace EduScroll.Application.Exceptions;

public sealed class ConflictAppException : AppException
{
    public ConflictAppException(string message, string errorCode = "RESOURCE_CONFLICT")
        : base(AppErrorKind.Conflict, errorCode, message)
    {
    }
}
