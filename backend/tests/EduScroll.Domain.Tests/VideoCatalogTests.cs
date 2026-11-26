using EduScroll.Domain.Entities;
using EduScroll.Domain.Exceptions;

namespace EduScroll.Domain.Tests;

public sealed class VideoCatalogTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 26, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhitelistedChannel_CreateAndMutate_WithValidInputs_ShouldSucceed()
    {
        var channel = WhitelistedChannel.Create(
            youTubeChannelId: "  UC_x5XG1OV2P6uZZ5FSM9Ttw  ",
            name: "  3Blue1Brown  ",
            language: "EN",
            topicCategory: "  Mathematics  ",
            trustScore: 0.95,
            createdAtUtc: FixedNow);

        Assert.NotEqual(Guid.Empty, channel.Id);
        Assert.Equal("UC_x5XG1OV2P6uZZ5FSM9Ttw", channel.YouTubeChannelId);
        Assert.Equal("3Blue1Brown", channel.Name);
        Assert.Equal("en", channel.Language);
        Assert.Equal("Mathematics", channel.TopicCategory);
        Assert.Equal(0.95, channel.TrustScore);
        Assert.True(channel.IsActive);
        Assert.Null(channel.LastSyncedAtUtc);
        Assert.Equal(FixedNow, channel.CreatedAtUtc);

        var syncedAt = FixedNow.AddHours(1);
        channel.MarkSynced(syncedAt);
        Assert.Equal(syncedAt, channel.LastSyncedAtUtc);

        channel.UpdateMetadata("Elzero Web School", "AR", "Programming", 0.9, false);
        Assert.Equal("Elzero Web School", channel.Name);
        Assert.Equal("ar", channel.Language);
        Assert.Equal("Programming", channel.TopicCategory);
        Assert.Equal(0.9, channel.TrustScore);
        Assert.False(channel.IsActive);
    }

    [Theory]
    [InlineData("", "Name", "en", "Math", 0.8, "CHANNEL_ID_REQUIRED")]
    [InlineData("UC123", "   ", "en", "Math", 0.8, "CHANNEL_NAME_REQUIRED")]
    [InlineData("UC123", "Name", "", "Math", 0.8, "CHANNEL_LANGUAGE_REQUIRED")]
    [InlineData("UC123", "Name", "fr", "Math", 0.8, "CHANNEL_LANGUAGE_INVALID")]
    [InlineData("UC123", "Name", "en", "", 0.8, "CHANNEL_CATEGORY_REQUIRED")]
    [InlineData("UC123", "Name", "en", "Math", -0.1, "CHANNEL_TRUST_SCORE_INVALID")]
    [InlineData("UC123", "Name", "en", "Math", 1.1, "CHANNEL_TRUST_SCORE_INVALID")]
    public void WhitelistedChannel_Create_WithInvalidInputs_ShouldThrow(
        string channelId,
        string name,
        string language,
        string category,
        double trustScore,
        string expectedCode)
    {
        var ex = Assert.Throws<InvalidVideoCatalogDomainException>(() =>
            WhitelistedChannel.Create(channelId, name, language, category, trustScore, FixedNow));

        Assert.Equal(expectedCode, ex.ErrorCode);
    }

    [Fact]
    public void CatalogVideo_CreateAndUpdateMetadata_WithValidInputs_ShouldSucceed()
    {
        var channelDbId = Guid.NewGuid();
        var published = FixedNow.AddDays(-2);

        var video = CatalogVideo.Create(
            youTubeVideoId: "  aircAruvnKk  ",
            whitelistedChannelId: channelDbId,
            channelExternalId: "UCYO_jab_esuFRV4b17AJtAw",
            channelName: "3Blue1Brown",
            title: "  But what is a neural network?  ",
            description: "Visual introduction to deep learning.",
            thumbnailUrl: "https://i.ytimg.com/vi/aircAruvnKk/hqdefault.jpg",
            language: "EN",
            durationSeconds: 1153,
            viewCount: 15_000_000,
            likeCount: 450_000,
            publishedAtUtc: published,
            discoverySource: DiscoverySource.WhitelistRss,
            utcNow: FixedNow);

        Assert.NotEqual(Guid.Empty, video.Id);
        Assert.Equal("aircAruvnKk", video.YouTubeVideoId);
        Assert.Equal(channelDbId, video.WhitelistedChannelId);
        Assert.Equal("But what is a neural network?", video.Title);
        Assert.Equal("en", video.Language);
        Assert.Equal(1153, video.DurationSeconds);
        Assert.Equal(DiscoverySource.WhitelistRss, video.DiscoverySource);

        // Update preserving duration & WhitelistedChannelId when RSS refresh omits them
        var refreshedAt = FixedNow.AddHours(3);
        video.UpdateFromMetadata(
            whitelistedChannelId: null,
            channelExternalId: "UCYO_jab_esuFRV4b17AJtAw",
            channelName: "3Blue1Brown",
            title: "But what is a neural network? (Updated)",
            description: null,
            thumbnailUrl: null,
            language: "en",
            durationSeconds: null,
            viewCount: 15_100_000,
            likeCount: 440_000,
            publishedAtUtc: published,
            discoverySource: DiscoverySource.YouTubeApiFallback,
            updatedAtUtc: refreshedAt);

        Assert.Equal(channelDbId, video.WhitelistedChannelId);
        Assert.Equal(1153, video.DurationSeconds);
        Assert.Equal(15_100_000, video.ViewCount);
        Assert.Equal(450_000, video.LikeCount);
        Assert.Equal(DiscoverySource.WhitelistRss, video.DiscoverySource);
        Assert.Equal(refreshedAt, video.UpdatedAtUtc);
    }

    [Fact]
    public void CatalogVideo_CreateFallbackWithNullableFields_ShouldSucceed()
    {
        var video = CatalogVideo.Create(
            youTubeVideoId: "arVideo0001",
            whitelistedChannelId: null,
            channelExternalId: "UC_Arabic_Edu",
            channelName: "قناة تعليمية",
            title: "شرح التفاضل والتكامل",
            description: null,
            thumbnailUrl: null,
            language: "ar",
            durationSeconds: null,
            viewCount: 0,
            likeCount: 0,
            publishedAtUtc: FixedNow,
            discoverySource: DiscoverySource.YouTubeApiFallback,
            utcNow: FixedNow);

        Assert.Null(video.WhitelistedChannelId);
        Assert.Null(video.DurationSeconds);
        Assert.Null(video.ThumbnailUrl);
        Assert.Equal(string.Empty, video.Description);
        Assert.Equal("ar", video.Language);
        Assert.Equal(DiscoverySource.YouTubeApiFallback, video.DiscoverySource);
    }

    [Theory]
    [InlineData("", "UC1", "Chan", "Title", "en", 60, 10, 5, null, "VIDEO_ID_REQUIRED")]
    [InlineData("vid12345678", "", "Chan", "Title", "en", 60, 10, 5, null, "VIDEO_CHANNEL_ID_REQUIRED")]
    [InlineData("vid12345678", "UC1", "", "Title", "en", 60, 10, 5, null, "VIDEO_CHANNEL_NAME_REQUIRED")]
    [InlineData("vid12345678", "UC1", "Chan", "", "en", 60, 10, 5, null, "VIDEO_TITLE_REQUIRED")]
    [InlineData("vid12345678", "UC1", "Chan", "Title", "fr", 60, 10, 5, null, "VIDEO_LANGUAGE_INVALID")]
    [InlineData("vid12345678", "UC1", "Chan", "Title", "en", -1, 10, 5, null, "VIDEO_DURATION_NEGATIVE")]
    [InlineData("vid12345678", "UC1", "Chan", "Title", "en", 60, -1, 5, null, "VIDEO_VIEW_COUNT_NEGATIVE")]
    [InlineData("vid12345678", "UC1", "Chan", "Title", "en", 60, 10, -1, null, "VIDEO_LIKE_COUNT_NEGATIVE")]
    [InlineData("vid12345678", "UC1", "Chan", "Title", "en", 60, 10, 5, "ftp://evil/thumb.jpg", "VIDEO_THUMBNAIL_URL_INVALID")]
    public void CatalogVideo_Create_WithInvalidInputs_ShouldThrow(
        string videoId,
        string channelExtId,
        string channelName,
        string title,
        string language,
        int? durationSeconds,
        long viewCount,
        long likeCount,
        string? thumbnailUrl,
        string expectedCode)
    {
        var ex = Assert.Throws<InvalidVideoCatalogDomainException>(() =>
            CatalogVideo.Create(
                videoId,
                null,
                channelExtId,
                channelName,
                title,
                "Desc",
                thumbnailUrl,
                language,
                durationSeconds,
                viewCount,
                likeCount,
                FixedNow,
                DiscoverySource.WhitelistRss,
                FixedNow));

        Assert.Equal(expectedCode, ex.ErrorCode);
    }

    [Fact]
    public void CatalogVideo_Create_WithEmptyGuidWhitelistedChannelId_ShouldThrow()
    {
        var ex = Assert.Throws<InvalidVideoCatalogDomainException>(() =>
            CatalogVideo.Create(
                "vid12345678",
                Guid.Empty,
                "UC1",
                "Channel",
                "Valid Title",
                "Desc",
                null,
                "en",
                120,
                100,
                10,
                FixedNow,
                DiscoverySource.WhitelistRss,
                FixedNow));

        Assert.Equal("VIDEO_INVALID_WHITELISTED_CHANNEL", ex.ErrorCode);
    }
}
