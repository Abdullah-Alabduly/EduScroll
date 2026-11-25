namespace EduScroll.Application.Exceptions;

public sealed class ForbiddenAppException : AppException
{
    public ForbiddenAppException(string message, string errorCode = "ACCESS_FORBIDDEN")
        : base(AppErrorKind.Forbidden, errorCode, message)
    {
    }
}
