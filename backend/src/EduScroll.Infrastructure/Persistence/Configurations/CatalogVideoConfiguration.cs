using EduScroll.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduScroll.Infrastructure.Persistence.Configurations;

public sealed class CatalogVideoConfiguration : IEntityTypeConfiguration<CatalogVideo>
{
    public void Configure(EntityTypeBuilder<CatalogVideo> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("CatalogVideos");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id)
            .ValueGeneratedNever();

        builder.Property(v => v.YouTubeVideoId)
            .IsRequired()
            .HasMaxLength(CatalogVideo.MaxYouTubeVideoIdLength)
            .IsUnicode(false);

        builder.Property(v => v.WhitelistedChannelId);

        builder.Property(v => v.ChannelExternalId)
            .IsRequired()
            .HasMaxLength(CatalogVideo.MaxChannelExternalIdLength)
            .IsUnicode(false);

        builder.Property(v => v.ChannelName)
            .IsRequired()
            .HasMaxLength(CatalogVideo.MaxChannelNameLength);

        builder.Property(v => v.Title)
            .IsRequired()
            .HasMaxLength(CatalogVideo.MaxTitleLength);

        // Intentionally unindexed large descriptive text column.
        builder.Property(v => v.Description)
            .IsRequired()
            .HasMaxLength(CatalogVideo.MaxDescriptionLength);

        builder.Property(v => v.ThumbnailUrl)
            .HasMaxLength(CatalogVideo.MaxThumbnailUrlLength);

        builder.Property(v => v.Language)
            .IsRequired()
            .HasMaxLength(CatalogVideo.MaxLanguageLength)
            .IsUnicode(false);

        builder.Property(v => v.DurationSeconds);

        builder.Property(v => v.ViewCount)
            .IsRequired();

        builder.Property(v => v.LikeCount)
            .IsRequired();

        builder.Property(v => v.PublishedAtUtc)
            .IsRequired();

        builder.Property(v => v.DiscoverySource)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(v => v.CreatedAtUtc)
            .IsRequired();

        builder.Property(v => v.UpdatedAtUtc)
            .IsRequired();

        builder.HasIndex(v => v.YouTubeVideoId)
            .IsUnique()
            .HasDatabaseName("IX_CatalogVideos_YouTubeVideoId");

        // Supports fast Database-First language-filtered retrieval ordered newest-first.
        builder.HasIndex(v => new { v.Language, v.PublishedAtUtc })
            .IsDescending(false, true)
            .HasDatabaseName("IX_CatalogVideos_Language_PublishedAtUtc");

        builder.HasIndex(v => v.WhitelistedChannelId)
            .HasDatabaseName("IX_CatalogVideos_WhitelistedChannelId");

        builder.HasOne<WhitelistedChannel>()
            .WithMany()
            .HasForeignKey(v => v.WhitelistedChannelId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
