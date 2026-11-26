namespace EduScroll.Application.Exceptions;

public sealed class UnprocessableEntityAppException : AppException
{
    public UnprocessableEntityAppException(string message, string errorCode = "UNPROCESSABLE_ENTITY")
        : base(AppErrorKind.UnprocessableEntity, errorCode, message)
    {
    }
}
