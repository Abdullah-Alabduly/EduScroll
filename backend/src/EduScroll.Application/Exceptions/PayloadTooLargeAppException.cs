namespace EduScroll.Application.Exceptions;

public sealed class PayloadTooLargeAppException : AppException
{
    public PayloadTooLargeAppException(string message, string errorCode = "PAYLOAD_TOO_LARGE")
        : base(AppErrorKind.PayloadTooLarge, errorCode, message)
    {
    }
}
