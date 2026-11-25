namespace EduScroll.Domain.Exceptions;

public sealed class InvalidRefreshTokenDomainException : DomainException
{
    public InvalidRefreshTokenDomainException(string errorCode, string message)
        : base(errorCode, message)
    {
    }
}
