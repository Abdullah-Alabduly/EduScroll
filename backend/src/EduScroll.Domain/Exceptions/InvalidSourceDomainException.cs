namespace EduScroll.Domain.Exceptions;

public sealed class InvalidSourceDomainException : DomainException
{
    public InvalidSourceDomainException(string errorCode, string message)
        : base(errorCode, message)
    {
    }
}
