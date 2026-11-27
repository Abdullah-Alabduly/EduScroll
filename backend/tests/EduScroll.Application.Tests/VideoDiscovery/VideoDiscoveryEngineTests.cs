using EduScroll.Application.Exceptions;
using EduScroll.Application.VideoDiscovery.Contracts;
using EduScroll.Application.VideoDiscovery.Models;
using EduScroll.Application.VideoDiscovery.Ports;
using EduScroll.Application.VideoDiscovery.Services;
using EduScroll.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduScroll.Application.Tests.VideoDiscovery;

public sealed class VideoDiscoveryEngineTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 27, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DiscoverAsync_WhenLocalCatalogMeetsThreshold_ShouldReturnLocalResultsWithoutCallingFallbackOrMetadata()
    {
        var localVideos = Enumerable.Range(1, 5)
            .Select(i => CreateCatalogVideo($"local_{i}", "en", $"Neural Networks {i}", FixedNow.AddHours(-i)))
            .ToArray();

        var repo = new InMemoryVideoCatalogRepository(localVideos);
        var fallbackClient = new StubSearchFallbackClient((_, _, _, _) =>
            throw new InvalidOperationException("Fallback should not be called when local results meet threshold."));
        var metadataClient = new StubVideoMetadataClient((_, _) =>
            throw new InvalidOperationException("Metadata client should not be called when fallback is skipped."));

        var engine = CreateEngine(
            repo,
            fallbackClient,
            metadataClient,
            new VideoDiscoveryLimits(MinLocalCandidatesThreshold: 3, EnableSearchFallback: true));

        var request = new VideoDiscoveryRequest(
            SearchQueries: [new VideoDiscoveryQueryInput("neural networks intuition", "en")],
            TargetFeedLanguage: TargetFeedLanguage.En,
            DesiredCandidateCount: 4);

        var result = await engine.DiscoverAsync(request, CancellationToken.None);

        Assert.False(result.FallbackTriggered);
        Assert.Null(result.FallbackSkippedReason);
        Assert.Equal(4, result.LocalCandidateCount);
        Assert.Equal(0, result.FallbackCandidateCount);
        Assert.Equal(4, result.Candidates.Count);
        Assert.All(result.Candidates, c => Assert.Equal("en", c.Language));
        Assert.Equal(0, fallbackClient.CallCount);
        Assert.Equal(0, metadataClient.CallCount);
        Assert.Equal(0, repo.UpsertCallCount);
    }

    [Fact]
    public async Task DiscoverAsync_WhenLocalBelowThreshold_ShouldTriggerFallbackEnrichPersistWriteThroughAndServeLocallyNextTime()
    {
        var existingLocal = CreateCatalogVideo("local_1", "en", "Eigenvalues Intro", FixedNow.AddHours(-1));
        var repo = new InMemoryVideoCatalogRepository(existingLocal);

        var fallbackClient = new StubSearchFallbackClient((query, lang, maxResults, _) =>
        {
            var items = new[]
            {
                CreateFallbackItem("local_1", lang, "Duplicate already local", FixedNow.AddHours(-1)),
                CreateFallbackItem("fb_vid_1", lang, $"Fallback {query} 1", FixedNow.AddMinutes(-30)),
                CreateFallbackItem("fb_vid_2", lang, $"Fallback {query} 2", FixedNow.AddMinutes(-20))
            };
            return Task.FromResult<IReadOnlyList<YouTubeSearchFallbackItem>>(items.Take(maxResults).ToArray());
        });

        var metadataClient = new StubVideoMetadataClient((videoIds, _) =>
        {
            var dict = videoIds.ToDictionary(
                id => id,
                id => new YouTubeVideoMetadataItem(
                    YouTubeVideoId: id,
                    ChannelExternalId: "UC_META",
                    ChannelName: "Enriched Channel",
                    Title: $"Eigenvalues Enriched {id}",
                    Description: $"Matrix transformation desc {id}",
                    ThumbnailUrl: $"https://i.ytimg.com/vi/{id}/hqdefault.jpg",
                    DefaultLanguage: "en-US",
                    DurationSeconds: 140,
                    ViewCount: 12_500,
                    LikeCount: 640,
                    PublishedAtUtc: FixedNow.AddMinutes(-15)),
                StringComparer.OrdinalIgnoreCase);

            return Task.FromResult<IReadOnlyDictionary<string, YouTubeVideoMetadataItem>>(dict);
        });

        var engine = CreateEngine(
            repo,
            fallbackClient,
            metadataClient,
            new VideoDiscoveryLimits(
                EnableSearchFallback: true,
                MinLocalCandidatesThreshold: 3,
                MaxFallbackQueriesPerDiscovery: 2,
                MaxFallbackResultsPerQuery: 5));

        var request = new VideoDiscoveryRequest(
            SearchQueries:
            [
                new VideoDiscoveryQueryInput("eigenvalues eigenvectors", "en"),
                new VideoDiscoveryQueryInput("matrix transformation", "en"),
                new VideoDiscoveryQueryInput("third query capped out", "en")
            ],
            TargetFeedLanguage: TargetFeedLanguage.En,
            DesiredCandidateCount: 5);

        // First call: below threshold (1 < 3) -> triggers search.list (capped at 2 queries), enriches new IDs, persists write-through
        var firstResult = await engine.DiscoverAsync(request, CancellationToken.None);

        Assert.True(firstResult.FallbackTriggered);
        Assert.Null(firstResult.FallbackSkippedReason);
        Assert.Equal(1, firstResult.LocalCandidateCount);
        Assert.Equal(2, firstResult.FallbackCandidateCount);
        Assert.Equal(3, firstResult.Candidates.Count);
        Assert.Equal(2, fallbackClient.CallCount);
        Assert.Equal(1, metadataClient.CallCount);
        Assert.Equal(2, metadataClient.LastBatchCount); // local_1 excluded before enrichment
        Assert.Equal(1, repo.UpsertCallCount);

        var fb1 = firstResult.Candidates.Single(c => c.YouTubeVideoId == "fb_vid_1");
        Assert.Equal(DiscoverySource.YouTubeApiFallback.ToString(), fb1.DiscoverySource);
        Assert.Equal(140, fb1.DurationSeconds);
        Assert.Equal(12_500L, fb1.ViewCount);
        Assert.Equal(640L, fb1.LikeCount);
        Assert.Equal("en", fb1.Language);

        // Verify persisted in local catalog repository with DiscoverySource.YouTubeApiFallback
        Assert.Equal(3, repo.StoredVideos.Count);
        Assert.Equal(DiscoverySource.YouTubeApiFallback, repo.StoredVideos["fb_vid_1"].DiscoverySource);

        // Second call: now 3 local videos meet threshold (3 >= 3) -> 0 fallback or metadata calls!
        var secondResult = await engine.DiscoverAsync(request, CancellationToken.None);

        Assert.False(secondResult.FallbackTriggered);
        Assert.Equal(3, secondResult.LocalCandidateCount);
        Assert.Equal(0, secondResult.FallbackCandidateCount);
        Assert.Equal(2, fallbackClient.CallCount);
        Assert.Equal(1, metadataClient.CallCount);
    }

    [Fact]
    public async Task DiscoverAsync_WithTargetLanguageBoth_ShouldMergeEnAndArCandidatesWithoutDuplicates()
    {
        var en1 = CreateCatalogVideo("shared_vid", "en", "Calculus EN 1", FixedNow.AddHours(-1));
        var en2 = CreateCatalogVideo("en_only_1", "en", "Calculus EN 2", FixedNow.AddHours(-2));
        var ar1 = CreateCatalogVideo("shared_vid", "ar", "Calculus AR Dup", FixedNow.AddHours(-1));
        var ar2 = CreateCatalogVideo("ar_only_1", "ar", "التفاضل والتكامل", FixedNow.AddHours(-3));

        var repo = new InMemoryVideoCatalogRepository(en1, en2, ar1, ar2);

        var fallbackClient = new StubSearchFallbackClient((_, lang, _, _) =>
        {
            var items = lang == "en"
                ? new[]
                {
                    CreateFallbackItem("en_only_1", "en", "Dup local EN", FixedNow),
                    CreateFallbackItem("fb_en_1", "en", "Fallback Calculus EN", FixedNow)
                }
                : new[]
                {
                    CreateFallbackItem("fb_en_1", "ar", "Cross-lang dup", FixedNow),
                    CreateFallbackItem("fb_ar_1", "ar", "شرح التكامل", FixedNow)
                };
            return Task.FromResult<IReadOnlyList<YouTubeSearchFallbackItem>>(items);
        });

        var metadataClient = new StubVideoMetadataClient((ids, _) =>
            Task.FromResult<IReadOnlyDictionary<string, YouTubeVideoMetadataItem>>(
                new Dictionary<string, YouTubeVideoMetadataItem>(StringComparer.OrdinalIgnoreCase)));

        var engine = CreateEngine(
            repo,
            fallbackClient,
            metadataClient,
            new VideoDiscoveryLimits(
                EnableSearchFallback: true,
                MinLocalCandidatesThreshold: 10,
                MaxFallbackQueriesPerDiscovery: 2,
                MaxFallbackResultsPerQuery: 5));

        var request = new VideoDiscoveryRequest(
            SearchQueries:
            [
                new VideoDiscoveryQueryInput("calculus derivatives", "en"),
                new VideoDiscoveryQueryInput("شرح التفاضل", "ar")
            ],
            TargetFeedLanguage: TargetFeedLanguage.Both,
            DesiredCandidateCount: 10);

        var result = await engine.DiscoverAsync(request, CancellationToken.None);

        Assert.True(result.FallbackTriggered);
        Assert.Equal(2, fallbackClient.CallCount);
        Assert.Contains(fallbackClient.RecordedLanguages, l => l == "en");
        Assert.Contains(fallbackClient.RecordedLanguages, l => l == "ar");

        // Local has 3 unique IDs (shared_vid, en_only_1, ar_only_1); fallback adds 2 unique IDs (fb_en_1, fb_ar_1)
        Assert.Equal(3, result.LocalCandidateCount);
        Assert.Equal(2, result.FallbackCandidateCount);
        Assert.Equal(5, result.Candidates.Count);
        Assert.Equal(5, result.Candidates.Select(c => c.YouTubeVideoId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(result.Candidates, c => c.Language == "en");
        Assert.Contains(result.Candidates, c => c.Language == "ar");
    }

    [Theory]
    [InlineData("YOUTUBE_API_QUOTA_EXCEEDED")]
    [InlineData("YOUTUBE_API_RATE_LIMITED")]
    [InlineData("YOUTUBE_API_KEY_MISSING")]
    public async Task DiscoverAsync_WhenFallbackFails_ShouldDegradeGracefullyAndReturnLocalCandidates(string errorCode)
    {
        var local = CreateCatalogVideo("local_1", "en", "Thermodynamics Basics", FixedNow.AddHours(-2));
        var repo = new InMemoryVideoCatalogRepository(local);

        var fallbackClient = new StubSearchFallbackClient((_, _, _, _) =>
            throw new ExternalDependencyAppException("Simulated upstream failure", errorCode));
        var metadataClient = new StubVideoMetadataClient((_, _) =>
            Task.FromResult<IReadOnlyDictionary<string, YouTubeVideoMetadataItem>>(
                new Dictionary<string, YouTubeVideoMetadataItem>()));

        var engine = CreateEngine(
            repo,
            fallbackClient,
            metadataClient,
            new VideoDiscoveryLimits(
                EnableSearchFallback: true,
                MinLocalCandidatesThreshold: 5,
                MaxFallbackQueriesPerDiscovery: 3));

        var request = new VideoDiscoveryRequest(
            SearchQueries:
            [
                new VideoDiscoveryQueryInput("thermodynamics", "en"),
                new VideoDiscoveryQueryInput("entropy laws", "en")
            ],
            TargetFeedLanguage: TargetFeedLanguage.En,
            DesiredCandidateCount: 10);

        var result = await engine.DiscoverAsync(request, CancellationToken.None);

        Assert.True(result.FallbackTriggered);
        Assert.Equal(errorCode, result.FallbackSkippedReason);
        Assert.Equal(1, result.LocalCandidateCount);
        Assert.Equal(0, result.FallbackCandidateCount);
        var single = Assert.Single(result.Candidates);
        Assert.Equal("local_1", single.YouTubeVideoId);
        // Terminal auth/quota/rate-limit breaks on first query without burning remaining queries
        Assert.Equal(1, fallbackClient.CallCount);
        Assert.Equal(0, metadataClient.CallCount);
    }

    [Fact]
    public async Task DiscoverAsync_WhenFallbackDisabledOrTimedOut_ShouldDegradeGracefully()
    {
        var local = CreateCatalogVideo("local_1", "en", "Quantum Mechanics", FixedNow.AddHours(-1));
        var repo = new InMemoryVideoCatalogRepository(local);
        var metadataClient = new StubVideoMetadataClient((_, _) =>
            Task.FromResult<IReadOnlyDictionary<string, YouTubeVideoMetadataItem>>(
                new Dictionary<string, YouTubeVideoMetadataItem>()));

        var disabledFallbackClient = new StubSearchFallbackClient((_, _, _, _) =>
            throw new InvalidOperationException("Must not call fallback when disabled."));
        var disabledEngine = CreateEngine(
            repo,
            disabledFallbackClient,
            metadataClient,
            new VideoDiscoveryLimits(EnableSearchFallback: false, MinLocalCandidatesThreshold: 5));

        var request = new VideoDiscoveryRequest(
            SearchQueries: [new VideoDiscoveryQueryInput("quantum mechanics", "en")],
            TargetFeedLanguage: TargetFeedLanguage.En,
            DesiredCandidateCount: 10);

        var disabledResult = await disabledEngine.DiscoverAsync(request, CancellationToken.None);
        Assert.False(disabledResult.FallbackTriggered);
        Assert.Equal("FALLBACK_DISABLED", disabledResult.FallbackSkippedReason);
        Assert.Equal(1, disabledResult.LocalCandidateCount);
        Assert.Equal(0, disabledFallbackClient.CallCount);

        var timeoutFallbackClient = new StubSearchFallbackClient((_, _, _, _) =>
            throw new GatewayTimeoutAppException("Timed out", "YOUTUBE_API_TIMEOUT"));
        var timeoutEngine = CreateEngine(
            repo,
            timeoutFallbackClient,
            metadataClient,
            new VideoDiscoveryLimits(EnableSearchFallback: true, MinLocalCandidatesThreshold: 5));

        var timeoutResult = await timeoutEngine.DiscoverAsync(request, CancellationToken.None);
        Assert.True(timeoutResult.FallbackTriggered);
        Assert.Equal("YOUTUBE_API_TIMEOUT", timeoutResult.FallbackSkippedReason);
        Assert.Equal(1, timeoutResult.LocalCandidateCount);
    }

    [Fact]
    public async Task DiscoverAsync_WithInvalidRequestOrCallerCancellation_ShouldThrowExpectedException()
    {
        var repo = new InMemoryVideoCatalogRepository();
        var fallbackClient = new StubSearchFallbackClient((_, _, _, _) =>
            Task.FromResult<IReadOnlyList<YouTubeSearchFallbackItem>>([]));
        var metadataClient = new StubVideoMetadataClient((_, _) =>
            Task.FromResult<IReadOnlyDictionary<string, YouTubeVideoMetadataItem>>(
                new Dictionary<string, YouTubeVideoMetadataItem>()));

        var engine = CreateEngine(repo, fallbackClient, metadataClient);

        var emptyQueriesEx = await Assert.ThrowsAsync<ValidationAppException>(() =>
            engine.DiscoverAsync(new VideoDiscoveryRequest([]), CancellationToken.None));
        Assert.Equal("VIDEO_DISCOVERY_QUERIES_REQUIRED", emptyQueriesEx.ErrorCode);

        var badLangEx = await Assert.ThrowsAsync<ValidationAppException>(() =>
            engine.DiscoverAsync(
                new VideoDiscoveryRequest([new VideoDiscoveryQueryInput("physics", "fr")]),
                CancellationToken.None));
        Assert.Equal("VIDEO_DISCOVERY_INVALID_LANGUAGE", badLangEx.ErrorCode);

        var badCountEx = await Assert.ThrowsAsync<ValidationAppException>(() =>
            engine.DiscoverAsync(
                new VideoDiscoveryRequest([new VideoDiscoveryQueryInput("physics", "en")], DesiredCandidateCount: 0),
                CancellationToken.None));
        Assert.Equal("VIDEO_DISCOVERY_INVALID_CANDIDATE_COUNT", badCountEx.ErrorCode);

        using var cts = new CancellationTokenSource();
        var cancelFallbackClient = new StubSearchFallbackClient((_, _, _, ct) =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<YouTubeSearchFallbackItem>>([]);
        });
        var cancelEngine = CreateEngine(repo, cancelFallbackClient, metadataClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancelEngine.DiscoverAsync(
                new VideoDiscoveryRequest([new VideoDiscoveryQueryInput("physics", "en")]),
                cts.Token));
    }

    private static VideoDiscoveryEngine CreateEngine(
        IVideoCatalogRepository repo,
        IYouTubeSearchFallbackClient fallbackClient,
        IYouTubeVideoMetadataClient metadataClient,
        VideoDiscoveryLimits? limits = null) =>
        new(
            repo,
            fallbackClient,
            metadataClient,
            limits ?? new VideoDiscoveryLimits(),
            new FixedTimeProvider(FixedNow),
            NullLogger<VideoDiscoveryEngine>.Instance);

    private static CatalogVideo CreateCatalogVideo(
        string videoId,
        string language,
        string title,
        DateTimeOffset publishedAtUtc) =>
        CatalogVideo.Create(
            youTubeVideoId: videoId,
            whitelistedChannelId: null,
            channelExternalId: "UC_TEST",
            channelName: "Edu Channel",
            title: title,
            description: $"Description for {title}",
            thumbnailUrl: $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg",
            language: language,
            durationSeconds: 180,
            viewCount: 1000,
            likeCount: 50,
            publishedAtUtc: publishedAtUtc,
            discoverySource: DiscoverySource.WhitelistRss,
            utcNow: FixedNow);

    private static YouTubeSearchFallbackItem CreateFallbackItem(
        string videoId,
        string language,
        string title,
        DateTimeOffset publishedAtUtc) =>
        new(
            YouTubeVideoId: videoId,
            ChannelExternalId: "UC_FALLBACK",
            ChannelName: "Fallback Channel",
            Title: title,
            Description: $"Fallback description for {title}",
            ThumbnailUrl: $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg",
            Language: language,
            PublishedAtUtc: publishedAtUtc);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubSearchFallbackClient(
        Func<string, string, int, CancellationToken, Task<IReadOnlyList<YouTubeSearchFallbackItem>>> handler)
        : IYouTubeSearchFallbackClient
    {
        public int CallCount { get; private set; }
        public List<string> RecordedLanguages { get; } = [];

        public Task<IReadOnlyList<YouTubeSearchFallbackItem>> SearchVideosAsync(
            string query,
            string relevanceLanguage,
            int maxResults,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            RecordedLanguages.Add(relevanceLanguage);
            return handler(query, relevanceLanguage, maxResults, cancellationToken);
        }
    }

    private sealed class StubVideoMetadataClient(
        Func<IReadOnlyCollection<string>, CancellationToken, Task<IReadOnlyDictionary<string, YouTubeVideoMetadataItem>>> handler)
        : IYouTubeVideoMetadataClient
    {
        public int CallCount { get; private set; }
        public int LastBatchCount { get; private set; }

        public Task<IReadOnlyDictionary<string, YouTubeVideoMetadataItem>> GetVideoMetadataAsync(
            IReadOnlyCollection<string> videoIds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastBatchCount = videoIds.Count;
            return handler(videoIds, cancellationToken);
        }
    }

    private sealed class InMemoryVideoCatalogRepository(params CatalogVideo[] seed) : IVideoCatalogRepository
    {
        private readonly List<CatalogVideo> _videos = [.. seed];

        public int UpsertCallCount { get; private set; }
        public Dictionary<string, CatalogVideo> StoredVideos =>
            _videos.GroupBy(v => v.YouTubeVideoId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        public Task<CatalogVideo?> GetByYouTubeVideoIdAsync(string youTubeVideoId, CancellationToken cancellationToken) =>
            Task.FromResult(_videos.FirstOrDefault(v =>
                string.Equals(v.YouTubeVideoId, youTubeVideoId, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<CatalogVideo>> GetByYouTubeVideoIdsAsync(
            IReadOnlyCollection<string> youTubeVideoIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CatalogVideo>>(
                _videos.Where(v => youTubeVideoIds.Contains(v.YouTubeVideoId, StringComparer.OrdinalIgnoreCase)).ToArray());

        public Task<int> UpsertRangeAsync(IReadOnlyList<CatalogVideo> videos, CancellationToken cancellationToken)
        {
            UpsertCallCount++;
            foreach (var video in videos)
            {
                var idx = _videos.FindIndex(v =>
                    string.Equals(v.YouTubeVideoId, video.YouTubeVideoId, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    _videos[idx] = video;
                }
                else
                {
                    _videos.Add(video);
                }
            }

            return Task.FromResult(videos.Count);
        }

        public Task<IReadOnlyList<CatalogVideo>> SearchByLanguageAsync(
            IReadOnlyCollection<string> languages,
            IReadOnlyCollection<string>? searchTerms,
            int maxResults,
            CancellationToken cancellationToken)
        {
            var langSet = languages.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var query = _videos.Where(v => langSet.Contains(v.Language));

            if (searchTerms is { Count: > 0 })
            {
                var terms = searchTerms.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
                if (terms.Length > 0)
                {
                    query = query.Where(v => terms.Any(t =>
                        v.Title.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                        v.Description.Contains(t, StringComparison.OrdinalIgnoreCase)));
                }
            }

            return Task.FromResult<IReadOnlyList<CatalogVideo>>(
                query.OrderByDescending(v => v.PublishedAtUtc)
                    .ThenByDescending(v => v.ViewCount)
                    .Take(maxResults)
                    .ToArray());
        }
    }
}
