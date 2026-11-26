namespace EduScroll.Domain.Exceptions;

public sealed class InvalidVideoCatalogDomainException : DomainException
{
    public InvalidVideoCatalogDomainException(string errorCode, string message)
        : base(errorCode, message)
    {
    }
}
