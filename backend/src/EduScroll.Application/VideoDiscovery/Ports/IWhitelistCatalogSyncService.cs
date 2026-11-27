using EduScroll.Application.VideoDiscovery.Contracts;

namespace EduScroll.Application.VideoDiscovery.Ports;

public interface IWhitelistCatalogSyncService
{
    Task<WhitelistSyncSummaryDto> SyncActiveChannelsAsync(CancellationToken cancellationToken);

    Task<WhitelistSyncSummaryDto> SyncActiveChannelsAsync(
        string? language,
        CancellationToken cancellationToken);
}
