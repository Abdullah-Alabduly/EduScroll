using EduScroll.Application.Exceptions;
using EduScroll.Application.Sources.Contracts;
using EduScroll.Application.Sources.Models;
using EduScroll.Application.Sources.Ports;
using EduScroll.Application.Sources.Services;
using EduScroll.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduScroll.Application.Tests.Sources;

public sealed class SourceServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 25, 19, 30, 0, TimeSpan.Zero);
    private static readonly string ValidBody =
        "Photosynthesis is the biological process by which green plants convert sunlight, water, and carbon dioxide into chemical energy.";

    [Fact]
    public void Normalizer_ShouldPreserveParagraphsAndStripControlCharacters()
    {
        var raw = "  First   paragraph\tline 1\r\nline 2\0\x07\r\n\r\n\r\n   Second   paragraph  ";

        var normalized = SourceTextNormalizer.Normalize(raw);

        Assert.Equal("First paragraph line 1\nline 2\n\nSecond paragraph", normalized);
    }

    [Fact]
    public async Task CreatePdfSourceAsync_WithValidRequest_ShouldPersistAndStripDirectoryFromFileName()
    {
        var repo = new InMemorySourceRepository();
        var service = CreateService(repo, new StubWebContentFetcher());
        var ownerId = Guid.NewGuid();

        var dto = await service.CreatePdfSourceAsync(
            ownerId,
            new CreatePdfSourceRequest(
                "  Biology Notes  ",
                "Pdf",
                ValidBody,
                @"C:\Users\Student\Documents\biology_ch1.pdf"),
            CancellationToken.None);

        Assert.Equal("Biology Notes", dto.Title);
        Assert.Equal("Pdf", dto.SourceType);
        Assert.Equal("biology_ch1.pdf", dto.OriginalFileName);
        Assert.Null(dto.OriginalUrl);
        Assert.Equal("Completed", dto.Status);
        Assert.Equal(ValidBody.Length, dto.ContentLength);
        Assert.Single(repo.Sources);
        Assert.Equal(ownerId, repo.Sources[0].OwnerUserId);
    }

    [Fact]
    public async Task CreatePdfSourceAsync_WithInvalidSourceType_ShouldThrowValidationException()
    {
        var service = CreateService(new InMemorySourceRepository(), new StubWebContentFetcher());

        var ex = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.CreatePdfSourceAsync(
                Guid.NewGuid(),
                new CreatePdfSourceRequest("Title", "WebArticle", ValidBody),
                CancellationToken.None));

        Assert.Equal("SOURCE_TYPE_INVALID", ex.ErrorCode);
        Assert.True(ex.Errors.ContainsKey("sourceType"));
    }

    [Fact]
    public async Task CreatePdfSourceAsync_WithTooShortText_ShouldThrowValidationException()
    {
        var service = CreateService(new InMemorySourceRepository(), new StubWebContentFetcher());

        var ex = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.CreatePdfSourceAsync(
                Guid.NewGuid(),
                new CreatePdfSourceRequest("Title", "Pdf", "Too short"),
                CancellationToken.None));

        Assert.Equal("SOURCE_TEXT_TOO_SHORT", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateUrlSourceAsync_WithValidFetchedArticle_ShouldPersistWebArticle()
    {
        var repo = new InMemorySourceRepository();
        var fetcher = new StubWebContentFetcher
        {
            Result = new WebFetchedArticle(
                "https://example.org/final-article",
                "Quantum Mechanics Basics",
                ValidBody,
                "text/html")
        };
        var service = CreateService(repo, fetcher);
        var ownerId = Guid.NewGuid();

        var dto = await service.CreateUrlSourceAsync(
            ownerId,
            new CreateUrlSourceRequest("https://example.org/article"),
            CancellationToken.None);

        Assert.Equal("Quantum Mechanics Basics", dto.Title);
        Assert.Equal("WebArticle", dto.SourceType);
        Assert.Equal("https://example.org/final-article", dto.OriginalUrl);
        Assert.Null(dto.OriginalFileName);
        Assert.Single(repo.Sources);
        Assert.Equal(ownerId, repo.Sources[0].OwnerUserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://example.org/file")]
    [InlineData("https://user:pass@example.org/article")]
    [InlineData("not a url")]
    public async Task CreateUrlSourceAsync_WithInvalidUrl_ShouldThrowValidationException(string badUrl)
    {
        var service = CreateService(new InMemorySourceRepository(), new StubWebContentFetcher());

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.CreateUrlSourceAsync(Guid.NewGuid(), new CreateUrlSourceRequest(badUrl), CancellationToken.None));
    }

    [Fact]
    public async Task CreateUrlSourceAsync_WhenExtractedArticleTooShort_ShouldThrowUnprocessableEntity()
    {
        var fetcher = new StubWebContentFetcher
        {
            Result = new WebFetchedArticle("https://example.org/a", "Short", "tiny", "text/html")
        };
        var service = CreateService(new InMemorySourceRepository(), fetcher);

        var ex = await Assert.ThrowsAsync<UnprocessableEntityAppException>(() =>
            service.CreateUrlSourceAsync(
                Guid.NewGuid(),
                new CreateUrlSourceRequest("https://example.org/a"),
                CancellationToken.None));

        Assert.Equal("ARTICLE_EXTRACTION_EMPTY", ex.ErrorCode);
    }

    [Fact]
    public async Task OwnershipEnforcement_ShouldIsolateGetListAndDeleteBetweenUsers()
    {
        var repo = new InMemorySourceRepository();
        var service = CreateService(repo, new StubWebContentFetcher());
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        var createdA = await service.CreatePdfSourceAsync(
            userA,
            new CreatePdfSourceRequest("User A Source", "Pdf", ValidBody),
            CancellationToken.None);

        // User B cannot view User A's source
        var getEx = await Assert.ThrowsAsync<NotFoundAppException>(() =>
            service.GetSourceByIdAsync(userB, createdA.Id, CancellationToken.None));
        Assert.Equal("SOURCE_NOT_FOUND", getEx.ErrorCode);

        // User B cannot delete User A's source
        var deleteEx = await Assert.ThrowsAsync<NotFoundAppException>(() =>
            service.DeleteSourceAsync(userB, createdA.Id, CancellationToken.None));
        Assert.Equal("SOURCE_NOT_FOUND", deleteEx.ErrorCode);

        // User B's list does not include User A's source
        var userBList = await service.GetUserSourcesAsync(userB, 1, 20, CancellationToken.None);
        Assert.Empty(userBList.Items);
        Assert.Equal(0, userBList.TotalCount);

        // User A can still view and delete their own source
        var loadedA = await service.GetSourceByIdAsync(userA, createdA.Id, CancellationToken.None);
        Assert.Equal(createdA.Id, loadedA.Id);

        await service.DeleteSourceAsync(userA, createdA.Id, CancellationToken.None);
        Assert.Empty(repo.Sources);
    }

    [Fact]
    public async Task GetUserSourcesAsync_ShouldPaginateAccurately()
    {
        var repo = new InMemorySourceRepository();
        var service = CreateService(repo, new StubWebContentFetcher());
        var owner = Guid.NewGuid();

        for (var i = 1; i <= 5; i++)
        {
            await service.CreatePdfSourceAsync(
                owner,
                new CreatePdfSourceRequest($"Source {i}", "Pdf", ValidBody),
                CancellationToken.None);
        }

        var page1 = await service.GetUserSourcesAsync(owner, 1, 2, CancellationToken.None);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(5, page1.TotalCount);
        Assert.True(page1.HasNextPage);

        var page3 = await service.GetUserSourcesAsync(owner, 3, 2, CancellationToken.None);
        Assert.Single(page3.Items);
        Assert.False(page3.HasNextPage);
    }

    private static SourceService CreateService(ISourceRepository repository, IWebContentFetcher fetcher) =>
        new(
            repository,
            fetcher,
            new SourceIngestionLimits(),
            new FixedTimeProvider(FixedNow),
            NullLogger<SourceService>.Instance);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubWebContentFetcher : IWebContentFetcher
    {
        public WebFetchedArticle Result { get; set; } = new(
            "https://example.org/default",
            "Default Article",
            ValidBody,
            "text/html");

        public Task<WebFetchedArticle> FetchAndExtractAsync(Uri url, CancellationToken cancellationToken)
            => Task.FromResult(Result);
    }

    private sealed class InMemorySourceRepository : ISourceRepository
    {
        public List<Source> Sources { get; } = [];

        public Task AddAsync(Source source, CancellationToken cancellationToken)
        {
            Sources.Add(source);
            return Task.CompletedTask;
        }

        public Task<Source?> GetByIdForOwnerAsync(Guid sourceId, Guid ownerUserId, CancellationToken cancellationToken)
        {
            var found = Sources.FirstOrDefault(s => s.Id == sourceId && s.OwnerUserId == ownerUserId);
            return Task.FromResult(found);
        }

        public Task<(IReadOnlyList<SourceListItem> Items, int TotalCount)> GetPagedForOwnerAsync(
            Guid ownerUserId,
            int page,
            int pageSize,
            int previewMaxLength,
            CancellationToken cancellationToken)
        {
            var owned = Sources
                .Where(s => s.OwnerUserId == ownerUserId)
                .OrderByDescending(s => s.CreatedAtUtc)
                .ThenByDescending(s => s.Id)
                .ToList();

            var items = owned
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new SourceListItem(
                    s.Id,
                    s.Title,
                    s.SourceType,
                    s.OriginalUrl,
                    s.OriginalFileName,
                    s.ContentLength,
                    s.ExtractedText.Length > previewMaxLength
                        ? s.ExtractedText[..previewMaxLength]
                        : s.ExtractedText,
                    s.Status,
                    s.CreatedAtUtc,
                    s.UpdatedAtUtc))
                .ToArray();

            return Task.FromResult<(IReadOnlyList<SourceListItem>, int)>((items, owned.Count));
        }

        public Task<bool> DeleteForOwnerAsync(Guid sourceId, Guid ownerUserId, CancellationToken cancellationToken)
        {
            var removed = Sources.RemoveAll(s => s.Id == sourceId && s.OwnerUserId == ownerUserId);
            return Task.FromResult(removed > 0);
        }
    }
}
