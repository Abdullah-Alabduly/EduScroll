using EduScroll.Domain.Exceptions;

namespace EduScroll.Domain.Entities;

/// <summary>
/// Represents a vetted educational YouTube channel curated for database-first discovery.
/// </summary>
public sealed class WhitelistedChannel
{
    public const int MaxYouTubeChannelIdLength = 64;
    public const int MaxNameLength = 200;
    public const int MaxLanguageLength = 8;
    public const int MaxTopicCategoryLength = 120;
    public const double MinTrustScore = 0.0;
    public const double MaxTrustScore = 1.0;

    private WhitelistedChannel()
    {
    }

    public Guid Id { get; private set; }

    public string YouTubeChannelId { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Language { get; private set; } = string.Empty;

    public string TopicCategory { get; private set; } = string.Empty;

    public double TrustScore { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset? LastSyncedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static WhitelistedChannel Create(
        string youTubeChannelId,
        string name,
        string language,
        string topicCategory,
        double trustScore,
        DateTimeOffset createdAtUtc,
        bool isActive = true)
    {
        var normalizedChannelId = ValidateRequired(
            youTubeChannelId,
            MaxYouTubeChannelIdLength,
            "CHANNEL_ID_REQUIRED",
            "CHANNEL_ID_TOO_LONG",
            "YouTube channel ID");

        var normalizedName = ValidateRequired(
            name,
            MaxNameLength,
            "CHANNEL_NAME_REQUIRED",
            "CHANNEL_NAME_TOO_LONG",
            "Channel name");

        var normalizedLanguage = ValidateLanguage(language);

        var normalizedCategory = ValidateRequired(
            topicCategory,
            MaxTopicCategoryLength,
            "CHANNEL_CATEGORY_REQUIRED",
            "CHANNEL_CATEGORY_TOO_LONG",
            "Topic category");

        var normalizedTrustScore = ValidateTrustScore(trustScore);

        return new WhitelistedChannel
        {
            Id = Guid.NewGuid(),
            YouTubeChannelId = normalizedChannelId,
            Name = normalizedName,
            Language = normalizedLanguage,
            TopicCategory = normalizedCategory,
            TrustScore = normalizedTrustScore,
            IsActive = isActive,
            LastSyncedAtUtc = null,
            CreatedAtUtc = createdAtUtc.ToUniversalTime()
        };
    }

    public void UpdateMetadata(
        string name,
        string language,
        string topicCategory,
        double trustScore,
        bool isActive)
    {
        var normalizedName = ValidateRequired(
            name,
            MaxNameLength,
            "CHANNEL_NAME_REQUIRED",
            "CHANNEL_NAME_TOO_LONG",
            "Channel name");

        var normalizedLanguage = ValidateLanguage(language);

        var normalizedCategory = ValidateRequired(
            topicCategory,
            MaxTopicCategoryLength,
            "CHANNEL_CATEGORY_REQUIRED",
            "CHANNEL_CATEGORY_TOO_LONG",
            "Topic category");

        var normalizedTrustScore = ValidateTrustScore(trustScore);

        Name = normalizedName;
        Language = normalizedLanguage;
        TopicCategory = normalizedCategory;
        TrustScore = normalizedTrustScore;
        IsActive = isActive;
    }

    public void MarkSynced(DateTimeOffset syncedAtUtc)
    {
        LastSyncedAtUtc = syncedAtUtc.ToUniversalTime();
    }

    private static string ValidateRequired(
        string? value,
        int maxLength,
        string requiredCode,
        string tooLongCode,
        string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidVideoCatalogDomainException(requiredCode, $"{label} is required.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new InvalidVideoCatalogDomainException(
                tooLongCode,
                $"{label} must not exceed {maxLength} characters.");
        }

        return trimmed;
    }

    private static string ValidateLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            throw new InvalidVideoCatalogDomainException(
                "CHANNEL_LANGUAGE_REQUIRED",
                "Channel language is required.");
        }

        var normalized = language.Trim().ToLowerInvariant();
        if (normalized is not (SourceSearchQuery.EnglishCode or SourceSearchQuery.ArabicCode))
        {
            throw new InvalidVideoCatalogDomainException(
                "CHANNEL_LANGUAGE_INVALID",
                "Channel language must be 'en' or 'ar'.");
        }

        return normalized;
    }

    private static double ValidateTrustScore(double trustScore)
    {
        if (double.IsNaN(trustScore) ||
            double.IsInfinity(trustScore) ||
            trustScore < MinTrustScore ||
            trustScore > MaxTrustScore)
        {
            throw new InvalidVideoCatalogDomainException(
                "CHANNEL_TRUST_SCORE_INVALID",
                $"Channel trust score must be between {MinTrustScore:0.0} and {MaxTrustScore:0.0}.");
        }

        return trustScore == 0.0 ? 0.0 : trustScore;
    }
}
