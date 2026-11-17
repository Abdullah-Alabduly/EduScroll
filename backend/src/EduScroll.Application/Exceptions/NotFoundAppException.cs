namespace EduScroll.Application.Exceptions;

public sealed class NotFoundAppException : AppException
{
    public NotFoundAppException(string message, string errorCode = "RESOURCE_NOT_FOUND")
        : base(AppErrorKind.NotFound, errorCode, message)
    {
    }
}
