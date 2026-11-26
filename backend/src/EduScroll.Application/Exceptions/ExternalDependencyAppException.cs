namespace EduScroll.Application.Exceptions;

public sealed class ExternalDependencyAppException : AppException
{
    public ExternalDependencyAppException(string message, string errorCode = "EXTERNAL_DEPENDENCY_FAILURE")
        : base(AppErrorKind.ExternalDependency, errorCode, message)
    {
    }
}
