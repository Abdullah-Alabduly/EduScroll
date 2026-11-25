namespace EduScroll.Application.Exceptions;

public sealed class UnauthorizedAppException : AppException
{
    public UnauthorizedAppException(string message, string errorCode = "UNAUTHORIZED")
        : base(AppErrorKind.Unauthorized, errorCode, message)
    {
    }
}
