using EduScroll.Application.Exceptions;
using EduScroll.Application.VideoDiscovery.Models;
using EduScroll.Application.VideoDiscovery.Ports;
using EduScroll.Application.VideoDiscovery.Services;
using EduScroll.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduScroll.Application.Tests.VideoDiscovery;

public sealed class WhitelistCatalogSyncServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 26, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SyncActiveChannelsAsync_ShouldIngestRssDeduplicateChunkIntoBatchesOf50UpsertAndUpdateLastSyncedAtUtc()
    {
        var channelA = WhitelistedChannel.Create("UC_CHAN_A", "3Blue1Brown", "en", "Mathematics", 0.95, FixedNow.AddDays(-10));
        var channelB = WhitelistedChannel.Create("UC_CHAN_B", "Elzero Web School", "ar", "Computer Science", 0.92, FixedNow.AddDays(-5));
        var inactiveChannel = WhitelistedChannel.Create("UC_INACTIVE", "Old Channel", "en", "Physics", 0.5, FixedNow.AddDays(-20), isActive: false);

        var channelRepo = new InMemoryWhitelistedChannelRepository(channelA, channelB, inactiveChannel);
        var videoRepo = new InMemoryVideoCatalogRepository();

        // Channel A returns 35 unique videos; Channel B returns 25 videos including 5 overlapping video IDs -> 55 unique IDs total.
        var channelAItems = Enumerable.Range(1, 35)
            .Select(i => CreateRssItem($"vid_{i:D3}", channelA.YouTubeChannelId, channelA.Name, $"A Title {i}", FixedNow.AddHours(-i)))
            .ToArray();
        var channelBItems = Enumerable.Range(31, 25)
            .Select(i => CreateRssItem($"vid_{i:D3}", channelB.YouTubeChannelId, channelB.Name, $"B Title {i}", FixedNow.AddHours(-i)))
            .ToArray();

        var rssClient = new StubYouTubeRssFeedClient(new Dictionary<string, Func<IReadOnlyList<YouTubeRssVideoItem>>>
        {
            [channelA.YouTubeChannelId] = () => channelAItems,
            [channelB.YouTubeChannelId] = () => channelBItems
        });

        var metadataClient = new StubYouTubeVideoMetadataClient((batch, _) =>
        {
            var dict = batch.ToDictionary(
                id => id,
                id => new YouTubeVideoMetadataItem(
                    YouTubeVideoId: id,
                    ChannelExternalId: null,
                    ChannelName: null,
                    Title: $"Enriched {id}",
                    Description: $"Enriched desc {id}",
                    ThumbnailUrl: $"https://i.ytimg.com/vi/{id}/hqdefault.jpg",
                    DefaultLanguage: id.EndsWith("55", StringComparison.Ordinal) ? "ar-EG" : "en-US",
                    DurationSeconds: 185,
                    ViewCount: 5000,
                    LikeCount: 250,
                    PublishedAtUtc: FixedNow.AddMinutes(-30)),
                StringComparer.OrdinalIgnoreCase);
            return Task.FromResult<IReadOnlyDictionary<string, YouTubeVideoMetadataItem>>(dict);
        });

        var service = CreateService(
            channelRepo,
            videoRepo,
            rssClient,
            metadataClient,
            new VideoDiscoveryLimits(MetadataBatchSize: 50, MaxVideosPerChannelPerSync: 40));

        var summary = await service.SyncActiveChannelsAsync(CancellationToken.None);

        Assert.Equal(2, summary.ActiveChannelsCount);
        Assert.Equal(2, summary.SucceededChannelsCount);
        Assert.Equal(0, summary.FailedChannelsCount);
        Assert.Equal(60, summary.FetchedRssEntriesCount);
        Assert.Equal(55, summary.DeduplicatedVideosCount);
        Assert.Equal(55, summary.EnrichedVideosCount);
        Assert.Equal(55, summary.UpsertedVideosCount);
        Assert.Equal(FixedNow, summary.CompletedAtUtc);

        // Verify chunking into batches of at most 50 (50 + 5)
        Assert.Equal(2, metadataClient.RecordedBatches.Count);
        Assert.Equal(50, metadataClient.RecordedBatches[0].Count);
        Assert.Equal(5, metadataClient.RecordedBatches[1].Count);

        // Verify repository upsert received 55 deduplicated CatalogVideo records
        Assert.Equal(1, videoRepo.UpsertCallCount);
        Assert.Equal(55, videoRepo.StoredVideos.Count);
        Assert.All(videoRepo.StoredVideos.Values, v => Assert.Equal(DiscoverySource.WhitelistRss, v.DiscoverySource));

        var firstStored = videoRepo.StoredVideos["vid_001"];
        Assert.Equal(channelA.Id, firstStored.WhitelistedChannelId);
        Assert.Equal("Enriched vid_001", firstStored.Title);
        Assert.Equal("en", firstStored.Language);
        Assert.Equal(185, firstStored.DurationSeconds);
        Assert.Equal(5000L, firstStored.ViewCount);
        Assert.Equal(250L, firstStored.LikeCount);

        var vid55 = videoRepo.StoredVideos["vid_055"];
        Assert.Equal("ar", vid55.Language);
        Assert.Equal(channelB.Id, vid55.WhitelistedChannelId);

        // Verify LastSyncedAtUtc updated ONLY for active succeeded channels
        Assert.Equal(FixedNow, channelRepo.GetSyncedAt(channelA.Id));
        Assert.Equal(FixedNow, channelRepo.GetSyncedAt(channelB.Id));
        Assert.Null(channelRepo.GetSyncedAt(inactiveChannel.Id));
    }

    [Fact]
    public async Task SyncActiveChannelsAsync_WhenOneChannelFails_ShouldContinueOtherChannelsAndMarkOnlySucceededAsSynced()
    {
        var healthyChannel = WhitelistedChannel.Create("UC_OK", "Healthy Channel", "en", "Science", 0.9, FixedNow.AddDays(-2));
        var failingChannel = WhitelistedChannel.Create("UC_FAIL", "Broken Channel", "en", "Math", 0.8, FixedNow.AddDays(-2));

        var channelRepo = new InMemoryWhitelistedChannelRepository(healthyChannel, failingChannel);
        var videoRepo = new InMemoryVideoCatalogRepository();

        var rssClient = new StubYouTubeRssFeedClient(new Dictionary<string, Func<IReadOnlyList<YouTubeRssVideoItem>>>
        {
            [healthyChannel.YouTubeChannelId] = () =>
            [
                CreateRssItem("healthy_vid_1", healthyChannel.YouTubeChannelId, healthyChannel.Name, "Healthy Video", FixedNow.AddHours(-1))
            ],
            [failingChannel.YouTubeChannelId] = () =>
                throw new ExternalDependencyAppException("Simulated RSS 503 failure", "YOUTUBE_RSS_FETCH_FAILED")
        });

        var metadataClient = new StubYouTubeVideoMetadataClient((batch, _) =>
        {
            var dict = new Dictionary<string, YouTubeVideoMetadataItem>(StringComparer.OrdinalIgnoreCase)
            {
                ["healthy_vid_1"] = new YouTubeVideoMetadataItem(
                    "healthy_vid_1",
                    healthyChannel.YouTubeChannelId,
                    healthyChannel.Name,
                    "Healthy Video Enriched",
                    "Enriched description",
                    null,
                    "en",
                    90,
                    1200,
                    80,
                    FixedNow.AddHours(-1))
            };
            return Task.FromResult<IReadOnlyDictionary<string, YouTubeVideoMetadataItem>>(dict);
        });

        var service = CreateService(channelRepo, videoRepo, rssClient, metadataClient);

        var summary = await service.SyncActiveChannelsAsync(CancellationToken.None);

        Assert.Equal(2, summary.ActiveChannelsCount);
        Assert.Equal(1, summary.SucceededChannelsCount);
        Assert.Equal(1, summary.FailedChannelsCount);
        Assert.Equal(1, summary.FetchedRssEntriesCount);
        Assert.Equal(1, summary.DeduplicatedVideosCount);
        Assert.Equal(1, summary.EnrichedVideosCount);
        Assert.Equal(1, summary.UpsertedVideosCount);

        Assert.Single(videoRepo.StoredVideos);
        Assert.True(videoRepo.StoredVideos.ContainsKey("healthy_vid_1"));
        Assert.Equal(FixedNow, channelRepo.GetSyncedAt(healthyChannel.Id));
        Assert.Null(channelRepo.GetSyncedAt(failingChannel.Id));
    }

    [Fact]
    public async Task SyncActiveChannelsAsync_WhenMetadataMissingOrEnrichmentFails_ShouldFallBackSafelyToRssMetadata()
    {
        var channel = WhitelistedChannel.Create("UC_AR_EDU", "Arabic Edu", "ar", "Physics", 0.9, FixedNow.AddDays(-1));
        var channelRepo = new InMemoryWhitelistedChannelRepository(channel);
        var videoRepo = new InMemoryVideoCatalogRepository();

        var rssClient = new StubYouTubeRssFeedClient(new Dictionary<string, Func<IReadOnlyList<YouTubeRssVideoItem>>>
        {
            [channel.YouTubeChannelId] = () =>
            [
                CreateRssItem("vid_partial", channel.YouTubeChannelId, channel.Name, "RSS Title 1", FixedNow.AddHours(-1), viewCount: 400),
                CreateRssItem("vid_missing", channel.YouTubeChannelId, channel.Name, "RSS Title 2", FixedNow.AddHours(-2), viewCount: 750)
            ]
        });

        // Batch returns only vid_partial with missing stats and non-en/ar language ("fr"), omitting vid_missing completely
        var partialMetadataClient = new StubYouTubeVideoMetadataClient((_, _) =>
        {
            var dict = new Dictionary<string, YouTubeVideoMetadataItem>(StringComparer.OrdinalIgnoreCase)
            {
                ["vid_partial"] = new YouTubeVideoMetadataItem(
                    "vid_partial",
                    null,
                    null,
                    null,
                    null,
                    null,
                    "fr",
                    null,
                    null,
                    null,
                    null)
            };
            return Task.FromResult<IReadOnlyDictionary<string, YouTubeVideoMetadataItem>>(dict);
        });

        var service = CreateService(channelRepo, videoRepo, rssClient, partialMetadataClient);
        var summary = await service.SyncActiveChannelsAsync("ar", CancellationToken.None);

        Assert.Equal(2, summary.DeduplicatedVideosCount);
        Assert.Equal(1, summary.EnrichedVideosCount);
        Assert.Equal(2, summary.UpsertedVideosCount);

        var partial = videoRepo.StoredVideos["vid_partial"];
        Assert.Equal("RSS Title 1", partial.Title);
        Assert.Equal("ar", partial.Language); // falls back to channel.Language
        Assert.Equal(400L, partial.ViewCount);
        Assert.Equal(0L, partial.LikeCount);
        Assert.Null(partial.DurationSeconds);

        var missing = videoRepo.StoredVideos["vid_missing"];
        Assert.Equal("RSS Title 2", missing.Title);
        Assert.Equal("ar", missing.Language);
        Assert.Equal(750L, missing.ViewCount);
        Assert.Equal("https://i.ytimg.com/vi/vid_missing/hqdefault.jpg", missing.ThumbnailUrl);

        // Also verify full enrichment exception (e.g., quota exceeded / missing API key) degrades gracefully to RSS-only upsert
        var throwingMetadataClient = new StubYouTubeVideoMetadataClient((_, _) =>
            throw new ExternalDependencyAppException("Quota exceeded", "YOUTUBE_API_QUOTA_EXCEEDED"));
        var videoRepo2 = new InMemoryVideoCatalogRepository();
        var fallbackService = CreateService(channelRepo, videoRepo2, rssClient, throwingMetadataClient);

        var fallbackSummary = await fallbackService.SyncActiveChannelsAsync(CancellationToken.None);
        Assert.Equal(0, fallbackSummary.EnrichedVideosCount);
        Assert.Equal(2, fallbackSummary.UpsertedVideosCount);
        Assert.Equal(2, videoRepo2.StoredVideos.Count);
    }

    [Fact]
    public async Task SyncActiveChannelsAsync_WhenNoActiveChannelsOrInvalidLanguageOrCancelled_ShouldBehaveCleanly()
    {
        var emptyChannelRepo = new InMemoryWhitelistedChannelRepository();
        var videoRepo = new InMemoryVideoCatalogRepository();
        var rssClient = new StubYouTubeRssFeedClient(new Dictionary<string, Func<IReadOnlyList<YouTubeRssVideoItem>>>());
        var metadataClient = new StubYouTubeVideoMetadataClient((_, _) =>
            Task.FromResult<IReadOnlyDictionary<string, YouTubeVideoMetadataItem>>(
                new Dictionary<string, YouTubeVideoMetadataItem>()));

        var service = CreateService(emptyChannelRepo, videoRepo, rssClient, metadataClient);

        var emptySummary = await service.SyncActiveChannelsAsync(CancellationToken.None);
        Assert.Equal(0, emptySummary.ActiveChannelsCount);
        Assert.Equal(0, emptySummary.UpsertedVideosCount);
        Assert.Equal(0, videoRepo.UpsertCallCount);
        Assert.Empty(metadataClient.RecordedBatches);

        var invalidLangEx = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.SyncActiveChannelsAsync("es", CancellationToken.None));
        Assert.Equal("VIDEO_LANGUAGE_INVALID", invalidLangEx.ErrorCode);

        var activeChannel = WhitelistedChannel.Create("UC_CANCEL", "Cancel Channel", "en", "CS", 0.9, FixedNow);
        var cancelChannelRepo = new InMemoryWhitelistedChannelRepository(activeChannel);
        using var cts = new CancellationTokenSource();
        var cancellingRssClient = new StubYouTubeRssFeedClient(new Dictionary<string, Func<IReadOnlyList<YouTubeRssVideoItem>>>
        {
            [activeChannel.YouTubeChannelId] = () =>
            {
                cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
                return [];
            }
        });

        var cancelService = CreateService(cancelChannelRepo, videoRepo, cancellingRssClient, metadataClient);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancelService.SyncActiveChannelsAsync(cts.Token));
        Assert.Null(cancelChannelRepo.GetSyncedAt(activeChannel.Id));
    }

    private static WhitelistCatalogSyncService CreateService(
        IWhitelistedChannelRepository channelRepo,
        IVideoCatalogRepository videoRepo,
        IYouTubeRssFeedClient rssClient,
        IYouTubeVideoMetadataClient metadataClient,
        VideoDiscoveryLimits? limits = null) =>
        new(
            channelRepo,
            videoRepo,
            rssClient,
            metadataClient,
            limits ?? new VideoDiscoveryLimits(),
            new FixedTimeProvider(FixedNow),
            NullLogger<WhitelistCatalogSyncService>.Instance);

    private static YouTubeRssVideoItem CreateRssItem(
        string videoId,
        string channelId,
        string channelName,
        string title,
        DateTimeOffset publishedAtUtc,
        long viewCount = 100) =>
        new(
            YouTubeVideoId: videoId,
            ChannelExternalId: channelId,
            ChannelName: channelName,
            Title: title,
            Description: $"RSS description for {videoId}",
            ThumbnailUrl: $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg",
            ViewCount: viewCount,
            PublishedAtUtc: publishedAtUtc);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubYouTubeRssFeedClient(
        IReadOnlyDictionary<string, Func<IReadOnlyList<YouTubeRssVideoItem>>> responsesByChannel) : IYouTubeRssFeedClient
    {
        public Task<IReadOnlyList<YouTubeRssVideoItem>> FetchChannelVideosAsync(
            string channelId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (responsesByChannel.TryGetValue(channelId, out var factory))
            {
                return Task.FromResult(factory());
            }

            return Task.FromResult<IReadOnlyList<YouTubeRssVideoItem>>([]);
        }
    }

    private sealed class StubYouTubeVideoMetadataClient(
        Func<IReadOnlyCollection<string>, CancellationToken, Task<IReadOnlyDictionary<string, YouTubeVideoMetadataItem>>> handler)
        : IYouTubeVideoMetadataClient
    {
        public List<IReadOnlyCollection<string>> RecordedBatches { get; } = [];

        public Task<IReadOnlyDictionary<string, YouTubeVideoMetadataItem>> GetVideoMetadataAsync(
            IReadOnlyCollection<string> videoIds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecordedBatches.Add(videoIds.ToArray());
            return handler(videoIds, cancellationToken);
        }
    }

    private sealed class InMemoryWhitelistedChannelRepository(params WhitelistedChannel[] channels)
        : IWhitelistedChannelRepository
    {
        private readonly List<WhitelistedChannel> _channels = [.. channels];
        private readonly Dictionary<Guid, DateTimeOffset> _syncedAtById = [];

        public DateTimeOffset? GetSyncedAt(Guid channelId) =>
            _syncedAtById.TryGetValue(channelId, out var ts) ? ts : null;

        public Task<WhitelistedChannel?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_channels.FirstOrDefault(c => c.Id == id));

        public Task<WhitelistedChannel?> GetByYouTubeChannelIdAsync(string youTubeChannelId, CancellationToken cancellationToken) =>
            Task.FromResult(_channels.FirstOrDefault(c => c.YouTubeChannelId == youTubeChannelId));

        public Task<IReadOnlyList<WhitelistedChannel>> GetActiveAsync(string? language, CancellationToken cancellationToken)
        {
            var query = _channels.Where(c => c.IsActive);
            if (!string.IsNullOrWhiteSpace(language))
            {
                query = query.Where(c => string.Equals(c.Language, language, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult<IReadOnlyList<WhitelistedChannel>>(query.ToArray());
        }

        public Task AddOrUpdateAsync(WhitelistedChannel channel, CancellationToken cancellationToken)
        {
            _channels.Add(channel);
            return Task.CompletedTask;
        }

        public Task MarkSyncedAsync(Guid channelId, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken)
        {
            _syncedAtById[channelId] = syncedAtUtc;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryVideoCatalogRepository : IVideoCatalogRepository
    {
        public int UpsertCallCount { get; private set; }
        public Dictionary<string, CatalogVideo> StoredVideos { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<CatalogVideo?> GetByYouTubeVideoIdAsync(string youTubeVideoId, CancellationToken cancellationToken) =>
            Task.FromResult(StoredVideos.TryGetValue(youTubeVideoId, out var video) ? video : null);

        public Task<IReadOnlyList<CatalogVideo>> GetByYouTubeVideoIdsAsync(
            IReadOnlyCollection<string> youTubeVideoIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CatalogVideo>>(
                youTubeVideoIds
                    .Where(id => StoredVideos.ContainsKey(id))
                    .Select(id => StoredVideos[id])
                    .ToArray());

        public Task<int> UpsertRangeAsync(IReadOnlyList<CatalogVideo> videos, CancellationToken cancellationToken)
        {
            UpsertCallCount++;
            foreach (var video in videos)
            {
                StoredVideos[video.YouTubeVideoId] = video;
            }

            return Task.FromResult(videos.Count);
        }

        public Task<IReadOnlyList<CatalogVideo>> SearchByLanguageAsync(
            IReadOnlyCollection<string> languages,
            IReadOnlyCollection<string>? searchTerms,
            int maxResults,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CatalogVideo>>([]);
    }
}
