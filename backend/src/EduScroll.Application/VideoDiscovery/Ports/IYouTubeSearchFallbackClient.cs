using EduScroll.Application.VideoDiscovery.Models;

namespace EduScroll.Application.VideoDiscovery.Ports;

public interface IYouTubeSearchFallbackClient
{
    Task<IReadOnlyList<YouTubeSearchFallbackItem>> SearchVideosAsync(
        string query,
        string relevanceLanguage,
        int maxResults,
        CancellationToken cancellationToken);
}
