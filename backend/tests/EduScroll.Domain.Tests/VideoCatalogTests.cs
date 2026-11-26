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
        Assert.Equal("Visual introduction to deep learning.", video.Description);
        Assert.Equal("https://i.ytimg.com/vi/aircAruvnKk/hqdefault.jpg", video.ThumbnailUrl);
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

    [Fact]
    public void WhitelistedChannel_Create_BoundaryValuesAndUtcNormalization_ShouldSucceed()
    {
        var nonUtcCreated = new DateTimeOffset(2026, 11, 26, 21, 0, 0, TimeSpan.FromHours(2));
        var nonUtcSynced = new DateTimeOffset(2026, 11, 26, 17, 30, 0, TimeSpan.FromHours(-5));

        var maxChannelId = new string('C', WhitelistedChannel.MaxYouTubeChannelIdLength);
        var maxName = new string('N', WhitelistedChannel.MaxNameLength);
        var maxCategory = new string('T', WhitelistedChannel.MaxTopicCategoryLength);

        var minTrustChannel = WhitelistedChannel.Create(
            maxChannelId,
            maxName,
            " en ",
            maxCategory,
            WhitelistedChannel.MinTrustScore,
            nonUtcCreated,
            isActive: false);

        Assert.Equal(maxChannelId, minTrustChannel.YouTubeChannelId);
        Assert.Equal(maxName, minTrustChannel.Name);
        Assert.Equal(maxCategory, minTrustChannel.TopicCategory);
        Assert.Equal(0.0, minTrustChannel.TrustScore);
        Assert.False(minTrustChannel.IsActive);
        Assert.Equal(TimeSpan.Zero, minTrustChannel.CreatedAtUtc.Offset);
        Assert.Equal(nonUtcCreated.UtcDateTime, minTrustChannel.CreatedAtUtc.UtcDateTime);

        minTrustChannel.MarkSynced(nonUtcSynced);
        Assert.NotNull(minTrustChannel.LastSyncedAtUtc);
        var syncedAtUtc = minTrustChannel.LastSyncedAtUtc!.Value;
        Assert.Equal(TimeSpan.Zero, syncedAtUtc.Offset);
        Assert.Equal(nonUtcSynced.UtcDateTime, syncedAtUtc.UtcDateTime);

        var maxTrustChannel = WhitelistedChannel.Create(
            "UC_MaxTrust",
            "Max Trust",
            "ar",
            "Physics",
            WhitelistedChannel.MaxTrustScore,
            FixedNow);

        Assert.Equal(1.0, maxTrustChannel.TrustScore);
    }

    [Theory]
    [MemberData(nameof(WhitelistedChannelBoundaryFailureCases))]
    public void WhitelistedChannel_Create_WithTooLongOrNonFiniteInputs_ShouldThrow(
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

    public static TheoryData<string, string, string, string, double, string> WhitelistedChannelBoundaryFailureCases => new()
    {
        { new string('C', WhitelistedChannel.MaxYouTubeChannelIdLength + 1), "Name", "en", "Math", 0.5, "CHANNEL_ID_TOO_LONG" },
        { "UC123", new string('N', WhitelistedChannel.MaxNameLength + 1), "en", "Math", 0.5, "CHANNEL_NAME_TOO_LONG" },
        { "UC123", "Name", "en", new string('T', WhitelistedChannel.MaxTopicCategoryLength + 1), 0.5, "CHANNEL_CATEGORY_TOO_LONG" },
        { "UC123", "Name", "en", "Math", double.NaN, "CHANNEL_TRUST_SCORE_INVALID" },
        { "UC123", "Name", "en", "Math", double.PositiveInfinity, "CHANNEL_TRUST_SCORE_INVALID" },
        { "UC123", "Name", "en", "Math", double.NegativeInfinity, "CHANNEL_TRUST_SCORE_INVALID" }
    };

    [Fact]
    public void WhitelistedChannel_UpdateMetadata_WithInvalidInputs_ShouldThrowAndPreserveExistingState()
    {
        var channel = WhitelistedChannel.Create("UC123", "Valid Name", "en", "Mathematics", 0.85, FixedNow, isActive: true);

        var badLang = Assert.Throws<InvalidVideoCatalogDomainException>(() =>
            channel.UpdateMetadata("Changed Name", "fr", "Physics", 0.5, isActive: false));
        Assert.Equal("CHANNEL_LANGUAGE_INVALID", badLang.ErrorCode);

        var badTrust = Assert.Throws<InvalidVideoCatalogDomainException>(() =>
            channel.UpdateMetadata("Changed Name", "ar", "Physics", 1.5, isActive: false));
        Assert.Equal("CHANNEL_TRUST_SCORE_INVALID", badTrust.ErrorCode);

        // Prior valid state must remain untouched after failed updates.
        Assert.Equal("Valid Name", channel.Name);
        Assert.Equal("en", channel.Language);
        Assert.Equal("Mathematics", channel.TopicCategory);
        Assert.Equal(0.85, channel.TrustScore);
        Assert.True(channel.IsActive);
    }

    [Fact]
    public void CatalogVideo_Create_BoundaryValuesDescriptionTruncationAndUtcNormalization_ShouldSucceed()
    {
        var nonUtcPublished = new DateTimeOffset(2026, 11, 25, 14, 0, 0, TimeSpan.FromHours(3));
        var nonUtcNow = new DateTimeOffset(2026, 11, 26, 20, 0, 0, TimeSpan.FromHours(2));

        var maxVideoId = new string('V', CatalogVideo.MaxYouTubeVideoIdLength);
        var maxChannelId = new string('C', CatalogVideo.MaxChannelExternalIdLength);
        var maxChannelName = new string('N', CatalogVideo.MaxChannelNameLength);
        var maxTitle = new string('T', CatalogVideo.MaxTitleLength);
        var httpThumbPrefix = "http://cdn.example.com/";
        var maxThumbnailUrl = httpThumbPrefix + new string('a', CatalogVideo.MaxThumbnailUrlLength - httpThumbPrefix.Length);
        var surrogateBoundaryDescription = new string('d', CatalogVideo.MaxDescriptionLength - 1) + "\U0001F600extra";

        var video = CatalogVideo.Create(
            maxVideoId,
            null,
            maxChannelId,
            maxChannelName,
            maxTitle,
            $"   {surrogateBoundaryDescription}   ",
            maxThumbnailUrl,
            " AR ",
            durationSeconds: 0,
            viewCount: 0,
            likeCount: 0,
            publishedAtUtc: nonUtcPublished,
            discoverySource: DiscoverySource.WhitelistRss,
            utcNow: nonUtcNow);

        Assert.Equal(maxVideoId, video.YouTubeVideoId);
        Assert.Equal(maxChannelId, video.ChannelExternalId);
        Assert.Equal(maxChannelName, video.ChannelName);
        Assert.Equal(maxTitle, video.Title);
        Assert.Equal(maxThumbnailUrl, video.ThumbnailUrl);
        Assert.Equal("ar", video.Language);
        Assert.Equal(0, video.DurationSeconds);
        Assert.Equal(CatalogVideo.MaxDescriptionLength - 1, video.Description.Length);
        Assert.False(char.IsHighSurrogate(video.Description[^1]));
        Assert.Equal(TimeSpan.Zero, video.PublishedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, video.CreatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, video.UpdatedAtUtc.Offset);
        Assert.Equal(nonUtcPublished.UtcDateTime, video.PublishedAtUtc.UtcDateTime);
        Assert.Equal(nonUtcNow.UtcDateTime, video.CreatedAtUtc.UtcDateTime);

        var whitespaceThumbVideo = CatalogVideo.Create(
            "vidWhitespace",
            null,
            "UC1",
            "Channel",
            "Title",
            "   ",
            "   ",
            "en",
            null,
            0,
            0,
            FixedNow,
            DiscoverySource.YouTubeApiFallback,
            FixedNow);

        Assert.Equal(string.Empty, whitespaceThumbVideo.Description);
        Assert.Null(whitespaceThumbVideo.ThumbnailUrl);
    }

    [Theory]
    [MemberData(nameof(CatalogVideoAdditionalFailureCases))]
    public void CatalogVideo_Create_WithLengthLanguageThumbnailOrSourceViolations_ShouldThrow(
        string videoId,
        string channelExtId,
        string channelName,
        string title,
        string language,
        string? thumbnailUrl,
        DiscoverySource discoverySource,
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
                60,
                10,
                1,
                FixedNow,
                discoverySource,
                FixedNow));

        Assert.Equal(expectedCode, ex.ErrorCode);
    }

    public static TheoryData<string, string, string, string, string, string?, DiscoverySource, string> CatalogVideoAdditionalFailureCases => new()
    {
        { new string('V', CatalogVideo.MaxYouTubeVideoIdLength + 1), "UC1", "Chan", "Title", "en", null, DiscoverySource.WhitelistRss, "VIDEO_ID_TOO_LONG" },
        { "vid123", new string('C', CatalogVideo.MaxChannelExternalIdLength + 1), "Chan", "Title", "en", null, DiscoverySource.WhitelistRss, "VIDEO_CHANNEL_ID_TOO_LONG" },
        { "vid123", "UC1", new string('N', CatalogVideo.MaxChannelNameLength + 1), "Title", "en", null, DiscoverySource.WhitelistRss, "VIDEO_CHANNEL_NAME_TOO_LONG" },
        { "vid123", "UC1", "Chan", new string('T', CatalogVideo.MaxTitleLength + 1), "en", null, DiscoverySource.WhitelistRss, "VIDEO_TITLE_TOO_LONG" },
        { "vid123", "UC1", "Chan", "Title", "   ", null, DiscoverySource.WhitelistRss, "VIDEO_LANGUAGE_REQUIRED" },
        { "vid123", "UC1", "Chan", "Title", "en", "https://i.ytimg.com/" + new string('a', CatalogVideo.MaxThumbnailUrlLength), DiscoverySource.WhitelistRss, "VIDEO_THUMBNAIL_URL_TOO_LONG" },
        { "vid123", "UC1", "Chan", "Title", "en", "/relative/thumb.jpg", DiscoverySource.WhitelistRss, "VIDEO_THUMBNAIL_URL_INVALID" },
        { "vid123", "UC1", "Chan", "Title", "en", "https://", DiscoverySource.WhitelistRss, "VIDEO_THUMBNAIL_URL_INVALID" },
        { "vid123", "UC1", "Chan", "Title", "en", null, (DiscoverySource)999, "VIDEO_DISCOVERY_SOURCE_INVALID" }
    };

    [Fact]
    public void CatalogVideo_UpdateFromMetadata_ShouldUpgradePreserveAndFailAtomically()
    {
        var video = CatalogVideo.Create(
            "fallbackVid1",
            whitelistedChannelId: null,
            "UC_Chan",
            "Math Channel",
            "Initial Title",
            description: null,
            thumbnailUrl: null,
            language: "en",
            durationSeconds: null,
            viewCount: 100,
            likeCount: 10,
            publishedAtUtc: FixedNow.AddDays(-1),
            discoverySource: DiscoverySource.YouTubeApiFallback,
            utcNow: FixedNow);

        var whitelistedChannelId = Guid.NewGuid();
        var updatedNonUtc = new DateTimeOffset(2026, 11, 26, 22, 0, 0, TimeSpan.FromHours(2));

        // Upgrade from YouTubeApiFallback to WhitelistRss with richer metadata.
        video.UpdateFromMetadata(
            whitelistedChannelId,
            "UC_Chan",
            "Math Channel",
            "Enriched Title",
            "Detailed calculus walkthrough.",
            "https://i.ytimg.com/vi/fallbackVid1/hqdefault.jpg",
            "EN",
            durationSeconds: 320,
            viewCount: 250,
            likeCount: 25,
            publishedAtUtc: FixedNow.AddDays(-1),
            discoverySource: DiscoverySource.WhitelistRss,
            updatedAtUtc: updatedNonUtc);

        Assert.Equal(whitelistedChannelId, video.WhitelistedChannelId);
        Assert.Equal(DiscoverySource.WhitelistRss, video.DiscoverySource);
        Assert.Equal("Enriched Title", video.Title);
        Assert.Equal("Detailed calculus walkthrough.", video.Description);
        Assert.Equal("https://i.ytimg.com/vi/fallbackVid1/hqdefault.jpg", video.ThumbnailUrl);
        Assert.Equal(320, video.DurationSeconds);
        Assert.Equal(TimeSpan.Zero, video.UpdatedAtUtc.Offset);

        // Subsequent partial RSS refresh with empty description and null thumbnail must not wipe existing metadata.
        video.UpdateFromMetadata(
            whitelistedChannelId: null,
            "UC_Chan",
            "Math Channel",
            "Enriched Title v2",
            description: "",
            thumbnailUrl: null,
            language: "en",
            durationSeconds: null,
            viewCount: 300,
            likeCount: 20,
            publishedAtUtc: FixedNow.AddDays(-1),
            discoverySource: DiscoverySource.WhitelistRss,
            updatedAtUtc: FixedNow.AddHours(4));

        Assert.Equal("Enriched Title v2", video.Title);
        Assert.Equal("Detailed calculus walkthrough.", video.Description);
        Assert.Equal("https://i.ytimg.com/vi/fallbackVid1/hqdefault.jpg", video.ThumbnailUrl);
        Assert.Equal(320, video.DurationSeconds);
        Assert.Equal(300, video.ViewCount);
        Assert.Equal(25, video.LikeCount);

        // Invalid updates must throw and leave entity state unchanged.
        var emptyChannelEx = Assert.Throws<InvalidVideoCatalogDomainException>(() =>
            video.UpdateFromMetadata(
                Guid.Empty,
                "UC_Other",
                "Other Channel",
                "Broken Title",
                "New Desc",
                null,
                "ar",
                100,
                999,
                99,
                FixedNow,
                DiscoverySource.WhitelistRss,
                FixedNow.AddHours(5)));
        Assert.Equal("VIDEO_INVALID_WHITELISTED_CHANNEL", emptyChannelEx.ErrorCode);

        var invalidSourceEx = Assert.Throws<InvalidVideoCatalogDomainException>(() =>
            video.UpdateFromMetadata(
                null,
                "UC_Other",
                "Other Channel",
                "Broken Title",
                "New Desc",
                null,
                "ar",
                100,
                999,
                99,
                FixedNow,
                (DiscoverySource)0,
                FixedNow.AddHours(5)));
        Assert.Equal("VIDEO_DISCOVERY_SOURCE_INVALID", invalidSourceEx.ErrorCode);

        Assert.Equal("UC_Chan", video.ChannelExternalId);
        Assert.Equal("Math Channel", video.ChannelName);
        Assert.Equal("Enriched Title v2", video.Title);
        Assert.Equal("Detailed calculus walkthrough.", video.Description);
        Assert.Equal("en", video.Language);
        Assert.Equal(300, video.ViewCount);
        Assert.Equal(FixedNow.AddHours(4), video.UpdatedAtUtc);
    }
}
