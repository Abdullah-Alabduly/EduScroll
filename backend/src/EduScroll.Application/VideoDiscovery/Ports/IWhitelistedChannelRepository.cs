using EduScroll.Domain.Entities;

namespace EduScroll.Application.VideoDiscovery.Ports;

public interface IWhitelistedChannelRepository
{
    Task<WhitelistedChannel?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<WhitelistedChannel?> GetByYouTubeChannelIdAsync(
        string youTubeChannelId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WhitelistedChannel>> GetActiveAsync(
        string? language,
        CancellationToken cancellationToken);

    Task AddOrUpdateAsync(WhitelistedChannel channel, CancellationToken cancellationToken);

    Task MarkSyncedAsync(
        Guid channelId,
        DateTimeOffset syncedAtUtc,
        CancellationToken cancellationToken);
}
