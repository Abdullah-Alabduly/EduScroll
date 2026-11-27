namespace EduScroll.Application.VideoDiscovery.Contracts;

public sealed record WhitelistSyncSummaryDto(
    int ActiveChannelsCount,
    int SucceededChannelsCount,
    int FailedChannelsCount,
    int FetchedRssEntriesCount,
    int DeduplicatedVideosCount,
    int EnrichedVideosCount,
    int UpsertedVideosCount,
    DateTimeOffset CompletedAtUtc);
