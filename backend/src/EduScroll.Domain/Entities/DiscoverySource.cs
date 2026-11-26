namespace EduScroll.Domain.Entities;

/// <summary>
/// Indicates how a <see cref="CatalogVideo"/> entry was discovered and ingested into the catalog.
/// </summary>
public enum DiscoverySource
{
    WhitelistRss = 1,
    YouTubeApiFallback = 2
}
