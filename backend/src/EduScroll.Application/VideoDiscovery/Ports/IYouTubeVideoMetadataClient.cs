using EduScroll.Application.VideoDiscovery.Models;

namespace EduScroll.Application.VideoDiscovery.Ports;

public interface IYouTubeVideoMetadataClient
{
    Task<IReadOnlyDictionary<string, YouTubeVideoMetadataItem>> GetVideoMetadataAsync(
        IReadOnlyCollection<string> videoIds,
        CancellationToken cancellationToken);
}
