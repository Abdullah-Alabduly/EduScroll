namespace EduScroll.Domain.Exceptions;

public sealed class InvalidSourceAnalysisDomainException : DomainException
{
    public InvalidSourceAnalysisDomainException(string errorCode, string message)
        : base(errorCode, message)
    {
    }
}
