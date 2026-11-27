using EduScroll.Application.AiAnalysis.Contracts;
using EduScroll.Application.AiAnalysis.Ports;
using EduScroll.Application.Exceptions;
using EduScroll.Application.Sources.Models;
using EduScroll.Application.Sources.Ports;
using EduScroll.Application.VideoDiscovery.Contracts;
using EduScroll.Application.VideoDiscovery.Models;
using EduScroll.Application.VideoDiscovery.Ports;
using EduScroll.Application.VideoDiscovery.Services;
using EduScroll.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduScroll.Application.Tests.VideoDiscovery;

public sealed class VideoDiscoveryServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 27, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MatchVideosForSourceAsync_WhenRequestIsNull_ShouldUseDefaultMaxCandidatesForwardAnalysisAndReturnMatchedResult()
    {
        var ownerUserId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();

        AnalyzeSourceRequest? capturedAnalyzeRequest = null;
        VideoDiscoveryRequest? capturedDiscoveryRequest = null;

        var analysisService = new StubSourceAnalysisService((uid, sid, req, _) =>
        {
            Assert.Equal(ownerUserId, uid);
            Assert.Equal(sourceId, sid);
            capturedAnalyzeRequest = req;

            return Task.FromResult(new SourceAnalysisDto(
                Id: analysisId,
                SourceId: sourceId,
                TargetFeedLanguage: "en",
                Subject: "Linear Algebra",
                Topic: "Eigenvalues",
                Summary: "Eigenvalues and eigenvectors explain invariant subspaces of linear transformations.",
                Topics: [new SourceAnalysisTopicDto("Eigenvalues", ["Characteristic Polynomial"])],
                Concepts: ["Eigenvalues", "Eigenvectors"],
                Keywords: ["linear algebra", "eigenvalues"],
                SearchQueries:
                [
                    new SourceSearchQueryDto("eigenvalues visual intuition", "en"),
                    new SourceSearchQueryDto("eigenvectors geometric meaning", "en")
                ],
                Provider: "Gemini",
                Model: "gemini-2.5-flash",
                PromptVersion: "v1",
                AnalyzedAtUtc: FixedNow,
                UpdatedAtUtc: FixedNow));
        });

        var candidate = new VideoCandidateDto(
            Id: Guid.NewGuid(),
            YouTubeVideoId: "abc123XYZ00",
            WhitelistedChannelId: null,
            ChannelExternalId: "UC_MATH",
            ChannelName: "3Blue1Brown",
            Title: "Eigenvectors and eigenvalues",
            Description: "Visual geometric explanation.",
            ThumbnailUrl: "https://i.ytimg.com/vi/abc123XYZ00/hqdefault.jpg",
            Language: "en",
            DurationSeconds: 980,
            ViewCount: 5_000_000,
            LikeCount: 250_000,
            PublishedAtUtc: FixedNow.AddDays(-5),
            DiscoverySource: "SearchFallback",
            WatchUrl: "https://www.youtube.com/watch?v=abc123XYZ00");

        var discoveryEngine = new StubVideoDiscoveryEngine((req, _) =>
        {
            capturedDiscoveryRequest = req;
            return Task.FromResult(new VideoDiscoveryResultDto(
                Candidates: [candidate],
                LocalCandidateCount: 0,
                FallbackCandidateCount: 1,
                FallbackTriggered: true,
                FallbackSkippedReason: null));
        });

        var sut = CreateService(analysisService, discoveryEngine);

        var result = await sut.MatchVideosForSourceAsync(ownerUserId, sourceId, null, CancellationToken.None);

        Assert.NotNull(capturedAnalyzeRequest);
        Assert.False(capturedAnalyzeRequest!.ForceRefresh);
        Assert.Null(capturedAnalyzeRequest.TargetFeedLanguage);

        Assert.NotNull(capturedDiscoveryRequest);
        Assert.Equal(10, capturedDiscoveryRequest!.DesiredCandidateCount);
        Assert.Equal(TargetFeedLanguage.En, capturedDiscoveryRequest.TargetFeedLanguage);
        Assert.Equal(2, capturedDiscoveryRequest.SearchQueries.Count);
        Assert.Equal(["linear algebra", "eigenvalues"], capturedDiscoveryRequest.AdditionalKeywords);

        Assert.Equal(sourceId, result.SourceId);
        Assert.Equal(analysisId, result.AnalysisId);
        Assert.Equal("Gemini", result.Provider);
        Assert.Equal("gemini-2.5-flash", result.Model);
        Assert.Equal("Linear Algebra", result.Subject);
        Assert.Equal("Eigenvalues", result.Topic);
        var matched = Assert.Single(result.Candidates);
        Assert.Equal("https://www.youtube.com/watch?v=abc123XYZ00", matched.WatchUrl);
        Assert.True(result.FallbackTriggered);
        Assert.Equal(1, result.FallbackCandidateCount);
    }

    [Fact]
    public async Task MatchVideosForSourceAsync_WhenForceReanalyzeAndBothLanguageSpecified_ShouldForwardParameters()
    {
        var ownerUserId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        AnalyzeSourceRequest? capturedAnalyzeRequest = null;
        VideoDiscoveryRequest? capturedDiscoveryRequest = null;

        var analysisService = new StubSourceAnalysisService((_, _, req, _) =>
        {
            capturedAnalyzeRequest = req;
            return Task.FromResult(new SourceAnalysisDto(
                Id: Guid.NewGuid(),
                SourceId: sourceId,
                TargetFeedLanguage: "both",
                Subject: "Physics",
                Topic: "Quantum Mechanics",
                Summary: "Wave-particle duality and superposition principles in quantum mechanics.",
                Topics: [new SourceAnalysisTopicDto("Quantum Mechanics", ["Superposition"])],
                Concepts: ["Superposition"],
                Keywords: ["quantum", "wavefunction"],
                SearchQueries:
                [
                    new SourceSearchQueryDto("quantum mechanics explained", "en"),
                    new SourceSearchQueryDto("شرح ميكانيكا الكم", "ar")
                ],
                Provider: "Gemini",
                Model: "gemini-2.5-flash",
                PromptVersion: "v1",
                AnalyzedAtUtc: FixedNow,
                UpdatedAtUtc: FixedNow));
        });

        var discoveryEngine = new StubVideoDiscoveryEngine((req, _) =>
        {
            capturedDiscoveryRequest = req;
            return Task.FromResult(new VideoDiscoveryResultDto([], 0, 0, false, "FALLBACK_DISABLED"));
        });

        var sut = CreateService(analysisService, discoveryEngine);

        var result = await sut.MatchVideosForSourceAsync(
            ownerUserId,
            sourceId,
            new MatchSourceVideosRequest(TargetFeedLanguage: "both", MaxCandidates: 15, ForceReanalyze: true),
            CancellationToken.None);

        Assert.True(capturedAnalyzeRequest!.ForceRefresh);
        Assert.Equal("both", capturedAnalyzeRequest.TargetFeedLanguage);
        Assert.Equal(TargetFeedLanguage.Both, capturedDiscoveryRequest!.TargetFeedLanguage);
        Assert.Equal(15, capturedDiscoveryRequest.DesiredCandidateCount);
        Assert.Equal("both", result.TargetFeedLanguage);
        Assert.Equal("FALLBACK_DISABLED", result.FallbackSkippedReason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    public async Task MatchVideosForSourceAsync_WhenMaxCandidatesOutOfRange_ShouldThrowValidationAppExceptionBeforeCallingDependencies(int invalidMax)
    {
        var analysisCalls = 0;
        var discoveryCalls = 0;

        var sut = CreateService(
            new StubSourceAnalysisService((_, _, _, _) =>
            {
                analysisCalls++;
                throw new InvalidOperationException("Should not run");
            }),
            new StubVideoDiscoveryEngine((_, _) =>
            {
                discoveryCalls++;
                throw new InvalidOperationException("Should not run");
            }));

        var ex = await Assert.ThrowsAsync<ValidationAppException>(() =>
            sut.MatchVideosForSourceAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new MatchSourceVideosRequest(MaxCandidates: invalidMax),
                CancellationToken.None));

        Assert.Equal("VIDEO_DISCOVERY_INVALID_CANDIDATE_COUNT", ex.ErrorCode);
        Assert.Equal(0, analysisCalls);
        Assert.Equal(0, discoveryCalls);
    }

    [Fact]
    public async Task MatchVideosForSourceAsync_WhenOwnerOrSourceEmpty_ShouldThrowUnauthorizedOrNotFound()
    {
        var sut = CreateService(
            new StubSourceAnalysisService((_, _, _, _) => throw new InvalidOperationException("Should not run")),
            new StubVideoDiscoveryEngine((_, _) => throw new InvalidOperationException("Should not run")));

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            sut.MatchVideosForSourceAsync(Guid.Empty, Guid.NewGuid(), null, CancellationToken.None));

        await Assert.ThrowsAsync<NotFoundAppException>(() =>
            sut.MatchVideosForSourceAsync(Guid.NewGuid(), Guid.Empty, null, CancellationToken.None));
    }

    private static VideoDiscoveryService CreateService(
        ISourceAnalysisService analysisService,
        IVideoDiscoveryEngine discoveryEngine) =>
        new(
            new NoOpSourceRepository(),
            new NoOpSourceAnalysisRepository(),
            analysisService,
            discoveryEngine,
            new NoOpWhitelistedChannelRepository(),
            new NoOpWhitelistCatalogSyncService(),
            TimeProvider.System,
            NullLogger<VideoDiscoveryService>.Instance);

    private sealed class StubSourceAnalysisService(
        Func<Guid, Guid, AnalyzeSourceRequest?, CancellationToken, Task<SourceAnalysisDto>> handler)
        : ISourceAnalysisService
    {
        public Task<SourceAnalysisDto> AnalyzeSourceAsync(
            Guid ownerUserId,
            Guid sourceId,
            AnalyzeSourceRequest? request,
            CancellationToken cancellationToken) =>
            handler(ownerUserId, sourceId, request, cancellationToken);

        public Task<SourceAnalysisDto> GetAnalysisBySourceIdAsync(
            Guid ownerUserId,
            Guid sourceId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubVideoDiscoveryEngine(
        Func<VideoDiscoveryRequest, CancellationToken, Task<VideoDiscoveryResultDto>> handler)
        : IVideoDiscoveryEngine
    {
        public Task<VideoDiscoveryResultDto> DiscoverAsync(
            VideoDiscoveryRequest request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }

    private sealed class NoOpSourceRepository : ISourceRepository
    {
        public Task AddAsync(Source source, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Source?> GetByIdForOwnerAsync(Guid sourceId, Guid ownerUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(IReadOnlyList<SourceListItem> Items, int TotalCount)> GetPagedForOwnerAsync(Guid ownerUserId, int page, int pageSize, int previewMaxLength, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteForOwnerAsync(Guid sourceId, Guid ownerUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoOpSourceAnalysisRepository : ISourceAnalysisRepository
    {
        public Task<SourceAnalysis?> GetBySourceIdForOwnerAsync(Guid sourceId, Guid ownerUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveOrReplaceAsync(SourceAnalysis analysis, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoOpWhitelistedChannelRepository : IWhitelistedChannelRepository
    {
        public Task<WhitelistedChannel?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WhitelistedChannel?> GetByYouTubeChannelIdAsync(string youTubeChannelId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WhitelistedChannel>> GetActiveAsync(string? language, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WhitelistedChannel>> ListAsync(string? language, bool? isActive, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(WhitelistedChannel channel, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddOrUpdateAsync(WhitelistedChannel channel, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkSyncedAsync(Guid channelId, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoOpWhitelistCatalogSyncService : IWhitelistCatalogSyncService
    {
        public Task<WhitelistSyncSummaryDto> SyncActiveChannelsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WhitelistSyncSummaryDto> SyncActiveChannelsAsync(string? language, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
