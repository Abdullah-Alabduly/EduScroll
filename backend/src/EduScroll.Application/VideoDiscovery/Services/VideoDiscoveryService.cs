using EduScroll.Application.AiAnalysis.Contracts;
using EduScroll.Application.AiAnalysis.Ports;
using EduScroll.Application.Exceptions;
using EduScroll.Application.Sources.Ports;
using EduScroll.Application.VideoDiscovery.Contracts;
using EduScroll.Application.VideoDiscovery.Ports;
using EduScroll.Domain.Entities;
using EduScroll.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EduScroll.Application.VideoDiscovery.Services;

public sealed class VideoDiscoveryService(
    ISourceRepository sourceRepository,
    ISourceAnalysisRepository sourceAnalysisRepository,
    ISourceAnalysisService sourceAnalysisService,
    IVideoDiscoveryEngine discoveryEngine,
    IWhitelistedChannelRepository channelRepository,
    IWhitelistCatalogSyncService whitelistSyncService,
    TimeProvider timeProvider,
    ILogger<VideoDiscoveryService> logger) : IVideoDiscoveryService
{
    private const int DefaultMaxCandidates = 20;
    private const int DefaultMatchCandidates = 10;
    private const int MaxAllowedCandidates = 100;

    public async Task<VideoDiscoveryResultDto> DiscoverForSourceAsync(
        Guid ownerUserId,
        Guid sourceId,
        DiscoverSourceVideosRequest? request,
        CancellationToken cancellationToken)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new UnauthorizedAppException(
                "Authenticated user identifier is missing or invalid.",
                "UNAUTHORIZED");
        }

        if (sourceId == Guid.Empty)
        {
            throw new NotFoundAppException(
                "The requested source was not found.",
                "SOURCE_NOT_FOUND");
        }

        var candidateLimit = ValidateMaxCandidates(request?.MaxCandidates, DefaultMaxCandidates);

        var source = await sourceRepository.GetByIdForOwnerAsync(sourceId, ownerUserId, cancellationToken);
        if (source is null || !source.IsOwnedBy(ownerUserId))
        {
            throw new NotFoundAppException(
                "The requested source was not found.",
                "SOURCE_NOT_FOUND");
        }

        var analysis = await sourceAnalysisRepository.GetBySourceIdForOwnerAsync(
            sourceId,
            ownerUserId,
            cancellationToken);

        if (analysis is null || !analysis.IsOwnedBy(ownerUserId))
        {
            throw new ConflictAppException(
                "Source has not been analyzed yet. Run AI content analysis before discovering videos.",
                "SOURCE_NOT_ANALYZED");
        }

        var searchQueries = analysis.SearchQueries
            .OrderBy(q => q.DisplayOrder)
            .Select(q => new VideoDiscoveryQueryInput(q.QueryText, q.Language))
            .ToArray();

        var discoveryRequest = new VideoDiscoveryRequest(
            SearchQueries: searchQueries,
            TargetFeedLanguage: analysis.TargetFeedLanguage,
            DesiredCandidateCount: candidateLimit,
            AdditionalKeywords: analysis.Keywords);

        logger.LogInformation(
            "Starting source video discovery. SourceId={SourceId} OwnerUserId={OwnerUserId} TargetFeedLanguage={TargetFeedLanguage} QueryCount={QueryCount} CandidateLimit={CandidateLimit}",
            sourceId,
            ownerUserId,
            analysis.TargetFeedLanguage,
            searchQueries.Length,
            candidateLimit);

        return await discoveryEngine.DiscoverAsync(discoveryRequest, cancellationToken);
    }

    public async Task<SourceVideoMatchResultDto> MatchVideosForSourceAsync(
        Guid ownerUserId,
        Guid sourceId,
        MatchSourceVideosRequest? request,
        CancellationToken cancellationToken)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new UnauthorizedAppException(
                "Authenticated user identifier is missing or invalid.",
                "UNAUTHORIZED");
        }

        if (sourceId == Guid.Empty)
        {
            throw new NotFoundAppException(
                "The requested source was not found.",
                "SOURCE_NOT_FOUND");
        }

        var candidateLimit = ValidateMaxCandidates(request?.MaxCandidates, DefaultMatchCandidates);

        var analysis = await sourceAnalysisService.AnalyzeSourceAsync(
            ownerUserId,
            sourceId,
            new AnalyzeSourceRequest(
                ForceRefresh: request?.ForceReanalyze ?? false,
                TargetFeedLanguage: request?.TargetFeedLanguage),
            cancellationToken);

        var targetFeedLanguage = ParseTargetFeedLanguage(analysis.TargetFeedLanguage);
        var searchQueries = analysis.SearchQueries
            .Select(q => new VideoDiscoveryQueryInput(q.Query, q.Language))
            .ToArray();

        var discoveryRequest = new VideoDiscoveryRequest(
            SearchQueries: searchQueries,
            TargetFeedLanguage: targetFeedLanguage,
            DesiredCandidateCount: candidateLimit,
            AdditionalKeywords: analysis.Keywords);

        var discovery = await discoveryEngine.DiscoverAsync(discoveryRequest, cancellationToken);

        logger.LogInformation(
            "Matched educational videos for source. SourceId={SourceId} OwnerUserId={OwnerUserId} AnalysisId={AnalysisId} Provider={Provider} TargetFeedLanguage={TargetFeedLanguage} CandidateCount={CandidateCount} FallbackTriggered={FallbackTriggered}",
            sourceId,
            ownerUserId,
            analysis.Id,
            analysis.Provider,
            analysis.TargetFeedLanguage,
            discovery.Candidates.Count,
            discovery.FallbackTriggered);

        return new SourceVideoMatchResultDto(
            SourceId: analysis.SourceId,
            AnalysisId: analysis.Id,
            TargetFeedLanguage: analysis.TargetFeedLanguage,
            Subject: analysis.Subject,
            Topic: analysis.Topic,
            Summary: analysis.Summary,
            SearchQueries: analysis.SearchQueries,
            Provider: analysis.Provider,
            Model: analysis.Model,
            Candidates: discovery.Candidates,
            LocalCandidateCount: discovery.LocalCandidateCount,
            FallbackCandidateCount: discovery.FallbackCandidateCount,
            FallbackTriggered: discovery.FallbackTriggered,
            FallbackSkippedReason: discovery.FallbackSkippedReason);
    }

    public async Task<IReadOnlyList<WhitelistedChannelDto>> ListWhitelistedChannelsAsync(
        string? language,
        bool? isActive,
        CancellationToken cancellationToken)
    {
        var normalizedLanguage = NormalizeOptionalLanguage(language);
        var channels = await channelRepository.ListAsync(normalizedLanguage, isActive, cancellationToken);

        return channels.Select(MapChannelDto).ToArray();
    }

    public async Task<WhitelistedChannelDto> CreateWhitelistedChannelAsync(
        CreateWhitelistedChannelRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ValidationAppException(
                "Channel creation request body is required.",
                new Dictionary<string, string[]>
                {
                    ["request"] = ["Request body is required."]
                },
                "VALIDATION_FAILED");
        }

        WhitelistedChannel channel;
        try
        {
            channel = WhitelistedChannel.Create(
                youTubeChannelId: request.YouTubeChannelId,
                name: request.Name,
                language: request.Language,
                topicCategory: request.TopicCategory,
                trustScore: request.TrustScore,
                createdAtUtc: timeProvider.GetUtcNow(),
                isActive: request.IsActive);
        }
        catch (InvalidVideoCatalogDomainException domainEx)
        {
            var field = MapChannelFieldName(domainEx.ErrorCode);
            throw new ValidationAppException(
                domainEx.Message,
                new Dictionary<string, string[]>
                {
                    [field] = [domainEx.Message]
                },
                domainEx.ErrorCode);
        }

        var existing = await channelRepository.GetByYouTubeChannelIdAsync(
            channel.YouTubeChannelId,
            cancellationToken);

        if (existing is not null)
        {
            throw new ConflictAppException(
                $"A whitelisted channel with YouTube channel ID '{channel.YouTubeChannelId}' already exists.",
                "CHANNEL_ALREADY_EXISTS");
        }

        await channelRepository.AddAsync(channel, cancellationToken);

        logger.LogInformation(
            "Created whitelisted channel. ChannelId={ChannelId} YouTubeChannelId={YouTubeChannelId} Language={Language} IsActive={IsActive}",
            channel.Id,
            channel.YouTubeChannelId,
            channel.Language,
            channel.IsActive);

        return MapChannelDto(channel);
    }

    public Task<WhitelistSyncSummaryDto> SyncWhitelistAsync(
        string? language,
        CancellationToken cancellationToken)
    {
        return whitelistSyncService.SyncActiveChannelsAsync(language, cancellationToken);
    }

    private static int ValidateMaxCandidates(int? maxCandidates, int defaultLimit)
    {
        if (!maxCandidates.HasValue)
        {
            return defaultLimit;
        }

        if (maxCandidates.Value is < 1 or > MaxAllowedCandidates)
        {
            throw new ValidationAppException(
                $"MaxCandidates must be between 1 and {MaxAllowedCandidates}.",
                new Dictionary<string, string[]>
                {
                    ["maxCandidates"] = [$"MaxCandidates must be between 1 and {MaxAllowedCandidates}."]
                },
                "VIDEO_DISCOVERY_INVALID_CANDIDATE_COUNT");
        }

        return maxCandidates.Value;
    }

    private static TargetFeedLanguage ParseTargetFeedLanguage(string? targetFeedLanguage) =>
        targetFeedLanguage?.Trim().ToLowerInvariant() switch
        {
            "ar" => TargetFeedLanguage.Ar,
            "both" => TargetFeedLanguage.Both,
            _ => TargetFeedLanguage.En
        };

    private static string? NormalizeOptionalLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var normalized = language.Trim().ToLowerInvariant();
        if (normalized is SourceSearchQuery.EnglishCode or SourceSearchQuery.ArabicCode)
        {
            return normalized;
        }

        throw new ValidationAppException(
            "Channel language must be 'en' or 'ar'.",
            new Dictionary<string, string[]>
            {
                ["language"] = ["Allowed values are 'en' and 'ar'."]
            },
            "CHANNEL_LANGUAGE_INVALID");
    }

    private static string MapChannelFieldName(string errorCode) =>
        errorCode switch
        {
            "CHANNEL_ID_REQUIRED" or "CHANNEL_ID_TOO_LONG" => "youTubeChannelId",
            "CHANNEL_NAME_REQUIRED" or "CHANNEL_NAME_TOO_LONG" => "name",
            "CHANNEL_LANGUAGE_REQUIRED" or "CHANNEL_LANGUAGE_INVALID" => "language",
            "CHANNEL_CATEGORY_REQUIRED" or "CHANNEL_CATEGORY_TOO_LONG" => "topicCategory",
            "CHANNEL_TRUST_SCORE_INVALID" => "trustScore",
            _ => "request"
        };

    private static WhitelistedChannelDto MapChannelDto(WhitelistedChannel channel) =>
        new(
            Id: channel.Id,
            YouTubeChannelId: channel.YouTubeChannelId,
            Name: channel.Name,
            Language: channel.Language,
            TopicCategory: channel.TopicCategory,
            TrustScore: channel.TrustScore,
            IsActive: channel.IsActive,
            LastSyncedAtUtc: channel.LastSyncedAtUtc,
            CreatedAtUtc: channel.CreatedAtUtc);
}
