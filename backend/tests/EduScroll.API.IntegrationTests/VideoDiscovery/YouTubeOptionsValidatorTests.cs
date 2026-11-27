using EduScroll.Infrastructure.Configuration;

namespace EduScroll.API.IntegrationTests.VideoDiscovery;

public sealed class YouTubeOptionsValidatorTests
{
    private readonly YouTubeOptionsValidator _validator = new();

    [Fact]
    public void Validate_WithDefaultOptions_ShouldSucceed()
    {
        var options = new YouTubeOptions();

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("AIzaSyTestValidConfiguredKey123")]
    public void Validate_WithEmptyOrValidApiKey_ShouldSucceed(string? apiKey)
    {
        var options = new YouTubeOptions
        {
            ApiKey = apiKey
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("REPLACE_WITH_YOUTUBE_DATA_API_V3_KEY")]
    [InlineData("replace_with_key")]
    public void Validate_WithPlaceholderApiKey_ShouldFailWithoutLeakingKeyValue(string placeholderKey)
    {
        var options = new YouTubeOptions
        {
            ApiKey = placeholderKey
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.NotNull(result.FailureMessage);
        Assert.DoesNotContain(placeholderKey, result.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.All(result.Failures!, failure =>
            Assert.DoesNotContain(placeholderKey, failure, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uri")]
    [InlineData("ftp://www.googleapis.com/youtube/v3")]
    [InlineData("/youtube/v3")]
    public void Validate_WithInvalidUrls_ShouldFail(string invalidUrl)
    {
        var options = new YouTubeOptions
        {
            ApiBaseUrl = invalidUrl,
            RssBaseUrl = invalidUrl
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.NotNull(result.Failures);
        Assert.Contains(result.Failures!, f => f.Contains("ApiBaseUrl", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, f => f.Contains("RssBaseUrl", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    public void Validate_WithOutOfRangeMetadataBatchSize_ShouldFail(int batchSize)
    {
        var options = new YouTubeOptions
        {
            MetadataBatchSize = batchSize
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("MetadataBatchSize", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void Validate_WithOutOfRangeTimeoutSeconds_ShouldFail(int timeoutSeconds)
    {
        var options = new YouTubeOptions
        {
            TimeoutSeconds = timeoutSeconds
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("TimeoutSeconds", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void Validate_WithOutOfRangeMaxRetries_ShouldFail(int maxRetries)
    {
        var options = new YouTubeOptions
        {
            MaxRetries = maxRetries
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("MaxRetries", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void Validate_WithOutOfRangeRetryBaseDelayMilliseconds_ShouldFail(int delayMs)
    {
        var options = new YouTubeOptions
        {
            RetryBaseDelayMilliseconds = delayMs
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("RetryBaseDelayMilliseconds", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Validate_WithOutOfRangeMaxVideosPerChannelPerSync_ShouldFail(int maxVideos)
    {
        var options = new YouTubeOptions
        {
            MaxVideosPerChannelPerSync = maxVideos
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("MaxVideosPerChannelPerSync", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithOutOfRangeMinLocalCandidatesThreshold_ShouldFail(int threshold)
    {
        var options = new YouTubeOptions
        {
            MinLocalCandidatesThreshold = threshold
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("MinLocalCandidatesThreshold", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Validate_WithOutOfRangeMaxFallbackQueriesPerDiscovery_ShouldFail(int maxQueries)
    {
        var options = new YouTubeOptions
        {
            MaxFallbackQueriesPerDiscovery = maxQueries
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("MaxFallbackQueriesPerDiscovery", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(26)]
    public void Validate_WithOutOfRangeMaxFallbackResultsPerQuery_ShouldFail(int maxResults)
    {
        var options = new YouTubeOptions
        {
            MaxFallbackResultsPerQuery = maxResults
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("MaxFallbackResultsPerQuery", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithBoundaryValidValues_ShouldSucceed()
    {
        var minOptions = new YouTubeOptions
        {
            ApiBaseUrl = "http://localhost:8080/youtube/v3",
            RssBaseUrl = "http://localhost:8080/feeds/videos.xml",
            TimeoutSeconds = 1,
            MaxRetries = 0,
            RetryBaseDelayMilliseconds = 0,
            MetadataBatchSize = 1,
            MaxVideosPerChannelPerSync = 1,
            EnableSearchFallback = false,
            MinLocalCandidatesThreshold = 1,
            MaxFallbackQueriesPerDiscovery = 1,
            MaxFallbackResultsPerQuery = 1
        };

        var maxOptions = new YouTubeOptions
        {
            ApiBaseUrl = "https://www.googleapis.com/youtube/v3",
            RssBaseUrl = "https://www.youtube.com/feeds/videos.xml",
            TimeoutSeconds = 120,
            MaxRetries = 5,
            RetryBaseDelayMilliseconds = 10_000,
            MetadataBatchSize = 50,
            MaxVideosPerChannelPerSync = 50,
            EnableSearchFallback = true,
            MinLocalCandidatesThreshold = 15,
            MaxFallbackQueriesPerDiscovery = 5,
            MaxFallbackResultsPerQuery = 25
        };

        Assert.True(_validator.Validate(null, minOptions).Succeeded);
        Assert.True(_validator.Validate(null, maxOptions).Succeeded);
    }
}
