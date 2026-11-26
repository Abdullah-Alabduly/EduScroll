namespace EduScroll.Application.Exceptions;

public sealed class GatewayTimeoutAppException : AppException
{
    public GatewayTimeoutAppException(string message, string errorCode = "GATEWAY_TIMEOUT")
        : base(AppErrorKind.Timeout, errorCode, message)
    {
    }
}
