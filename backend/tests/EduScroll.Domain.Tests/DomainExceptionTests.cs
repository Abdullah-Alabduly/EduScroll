using EduScroll.Domain.Exceptions;

namespace EduScroll.Domain.Tests;

public sealed class DomainExceptionTests
{
    private sealed class TestDomainException : DomainException
    {
        public TestDomainException(string errorCode, string message)
            : base(errorCode, message)
        {
        }

        public TestDomainException(string errorCode, string message, Exception innerException)
            : base(errorCode, message, innerException)
        {
        }
    }

    [Fact]
    public void Constructor_ShouldPreserveErrorCodeAndMessage()
    {
        var ex = new TestDomainException("TEST_CODE", "Invariant violated.");

        Assert.Equal("TEST_CODE", ex.ErrorCode);
        Assert.Equal("Invariant violated.", ex.Message);
    }

    [Fact]
    public void Constructor_ShouldPreserveInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new TestDomainException("TEST_CODE", "Invariant violated.", inner);

        Assert.Same(inner, ex.InnerException);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ShouldThrow_WhenErrorCodeIsEmptyOrWhitespace(string invalidCode)
    {
        Assert.Throws<ArgumentException>(() => new TestDomainException(invalidCode, "Valid message"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ShouldThrow_WhenMessageIsEmptyOrWhitespace(string invalidMessage)
    {
        Assert.Throws<ArgumentException>(() => new TestDomainException("VALID_CODE", invalidMessage));
    }
}
