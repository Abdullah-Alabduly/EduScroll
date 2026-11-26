using EduScroll.Domain.Entities;

namespace EduScroll.Application.VideoDiscovery.Ports;

public interface IVideoCatalogRepository
{
    Task<CatalogVideo?> GetByYouTubeVideoIdAsync(
        string youTubeVideoId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogVideo>> GetByYouTubeVideoIdsAsync(
        IReadOnlyCollection<string> youTubeVideoIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Inserts new catalog videos and refreshes existing metadata deduplicated by <see cref="CatalogVideo.YouTubeVideoId"/>.
    /// Returns the total number of distinct videos inserted or updated.
    /// </summary>
    Task<int> UpsertRangeAsync(
        IReadOnlyList<CatalogVideo> videos,
        CancellationToken cancellationToken);

    /// <summary>
    /// Searches persisted catalog videos filtered by allowed language codes (<c>"en"</c>/<c>"ar"</c>)
    /// and optional search terms, ordered newest-first using the <c>(Language, PublishedAtUtc)</c> index.
    /// </summary>
    Task<IReadOnlyList<CatalogVideo>> SearchByLanguageAsync(
        IReadOnlyCollection<string> languages,
        IReadOnlyCollection<string>? searchTerms,
        int maxResults,
        CancellationToken cancellationToken);
}
