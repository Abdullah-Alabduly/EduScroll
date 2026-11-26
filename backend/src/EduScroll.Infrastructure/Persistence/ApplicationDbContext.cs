using EduScroll.Domain.Entities;
using EduScroll.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EduScroll.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Source> Sources => Set<Source>();

    public DbSet<SourceAnalysis> SourceAnalyses => Set<SourceAnalysis>();

    public DbSet<SourceTopic> SourceTopics => Set<SourceTopic>();

    public DbSet<SourceSearchQuery> SourceSearchQueries => Set<SourceSearchQuery>();

    public DbSet<WhitelistedChannel> WhitelistedChannels => Set<WhitelistedChannel>();

    public DbSet<CatalogVideo> CatalogVideos => Set<CatalogVideo>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
