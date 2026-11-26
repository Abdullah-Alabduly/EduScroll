namespace EduScroll.Application.Exceptions;

/// <summary>
/// Transport-agnostic categories for expected application errors.
/// </summary>
public enum AppErrorKind
{
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Forbidden = 4,
    ExternalDependency = 5,
    Unauthorized = 6,
    UnprocessableEntity = 7,
    PayloadTooLarge = 8,
    Timeout = 9
}
