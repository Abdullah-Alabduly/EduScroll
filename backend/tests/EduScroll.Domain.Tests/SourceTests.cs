using EduScroll.Domain.Entities;
using EduScroll.Domain.Exceptions;

namespace EduScroll.Domain.Tests;

public sealed class SourceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 25, 19, 0, 0, TimeSpan.Zero);
    private static readonly string ValidText = new('A', Source.MinExtractedTextLength + 25);

    [Fact]
    public void CreatePdf_WithValidInputs_ShouldSetPropertiesAndOwnership()
    {
        var ownerId = Guid.NewGuid();

        var source = Source.CreatePdf(
            ownerId,
            "  Linear Algebra Chapter 1  ",
            $"  {ValidText}  ",
            FixedNow,
            "  chapter1.pdf  ");

        Assert.NotEqual(Guid.Empty, source.Id);
        Assert.Equal(ownerId, source.OwnerUserId);
        Assert.Equal("Linear Algebra Chapter 1", source.Title);
        Assert.Equal(SourceType.Pdf, source.SourceType);
        Assert.Null(source.OriginalUrl);
        Assert.Equal("chapter1.pdf", source.OriginalFileName);
        Assert.Equal(ValidText, source.ExtractedText);
        Assert.Equal(ValidText.Length, source.ContentLength);
        Assert.Equal(SourceStatus.Completed, source.Status);
        Assert.Equal(FixedNow, source.CreatedAtUtc);
        Assert.Equal(FixedNow, source.UpdatedAtUtc);
        Assert.True(source.IsOwnedBy(ownerId));
        Assert.False(source.IsOwnedBy(Guid.NewGuid()));
        Assert.False(source.IsOwnedBy(Guid.Empty));
    }

    [Fact]
    public void CreatePdf_WithEmptyOwner_ShouldThrowInvalidSourceDomainException()
    {
        var ex = Assert.Throws<InvalidSourceDomainException>(() =>
            Source.CreatePdf(Guid.Empty, "Title", ValidText, FixedNow));

        Assert.Equal("SOURCE_INVALID_OWNER", ex.ErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreatePdf_WithMissingTitle_ShouldThrow(string badTitle)
    {
        var ex = Assert.Throws<InvalidSourceDomainException>(() =>
            Source.CreatePdf(Guid.NewGuid(), badTitle, ValidText, FixedNow));

        Assert.Equal("SOURCE_TITLE_REQUIRED", ex.ErrorCode);
    }

    [Fact]
    public void CreatePdf_WithTooShortText_ShouldThrow()
    {
        var ex = Assert.Throws<InvalidSourceDomainException>(() =>
            Source.CreatePdf(Guid.NewGuid(), "Title", "Too short", FixedNow));

        Assert.Equal("SOURCE_TEXT_TOO_SHORT", ex.ErrorCode);
    }

    [Fact]
    public void CreatePdf_WithTextExceedingLimit_ShouldThrow()
    {
        var ex = Assert.Throws<InvalidSourceDomainException>(() =>
            Source.CreatePdf(Guid.NewGuid(), "Title", new string('x', 120), FixedNow, maxContentLength: 100));

        Assert.Equal("SOURCE_TEXT_TOO_LONG", ex.ErrorCode);
    }

    [Fact]
    public void CreateWebArticle_WithValidInputs_ShouldSucceed()
    {
        var ownerId = Guid.NewGuid();
        var source = Source.CreateWebArticle(
            ownerId,
            "Understanding Gradient Descent",
            "https://example.org/articles/gradient-descent",
            ValidText,
            FixedNow);

        Assert.Equal(SourceType.WebArticle, source.SourceType);
        Assert.Equal("https://example.org/articles/gradient-descent", source.OriginalUrl);
        Assert.Null(source.OriginalFileName);
        Assert.Equal(SourceStatus.Completed, source.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.org/file")]
    [InlineData("file:///etc/passwd")]
    public void CreateWebArticle_WithInvalidUrl_ShouldThrow(string badUrl)
    {
        Assert.Throws<InvalidSourceDomainException>(() =>
            Source.CreateWebArticle(Guid.NewGuid(), "Title", badUrl, ValidText, FixedNow));
    }
}
