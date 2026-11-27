using EduScroll.Application.VideoDiscovery.Models;

namespace EduScroll.Application.VideoDiscovery.Ports;

public interface IYouTubeRssFeedClient
{
    Task<IReadOnlyList<YouTubeRssVideoItem>> FetchChannelVideosAsync(
        string channelId,
        CancellationToken cancellationToken);
}
