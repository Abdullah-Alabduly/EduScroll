using EduScroll.Application.VideoDiscovery.Contracts;

namespace EduScroll.Application.VideoDiscovery.Ports;

public interface IVideoDiscoveryService
{
    Task<VideoDiscoveryResultDto> DiscoverForSourceAsync(
        Guid ownerUserId,
        Guid sourceId,
        DiscoverSourceVideosRequest? request,
        CancellationToken cancellationToken);

    Task<SourceVideoMatchResultDto> MatchVideosForSourceAsync(
        Guid ownerUserId,
        Guid sourceId,
        MatchSourceVideosRequest? request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WhitelistedChannelDto>> ListWhitelistedChannelsAsync(
        string? language,
        bool? isActive,
        CancellationToken cancellationToken);

    Task<WhitelistedChannelDto> CreateWhitelistedChannelAsync(
        CreateWhitelistedChannelRequest request,
        CancellationToken cancellationToken);

    Task<WhitelistSyncSummaryDto> SyncWhitelistAsync(
        string? language,
        CancellationToken cancellationToken);
}
