using EduScroll.Application.VideoDiscovery.Contracts;

namespace EduScroll.Application.VideoDiscovery.Ports;

public interface IVideoDiscoveryEngine
{
    Task<VideoDiscoveryResultDto> DiscoverAsync(
        VideoDiscoveryRequest request,
        CancellationToken cancellationToken);
}
