using EduScroll.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduScroll.Infrastructure.Persistence.Configurations;

public sealed class WhitelistedChannelConfiguration : IEntityTypeConfiguration<WhitelistedChannel>
{
    public void Configure(EntityTypeBuilder<WhitelistedChannel> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("WhitelistedChannels");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .ValueGeneratedNever();

        builder.Property(c => c.YouTubeChannelId)
            .IsRequired()
            .HasMaxLength(WhitelistedChannel.MaxYouTubeChannelIdLength)
            .IsUnicode(false);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(WhitelistedChannel.MaxNameLength);

        builder.Property(c => c.Language)
            .IsRequired()
            .HasMaxLength(WhitelistedChannel.MaxLanguageLength)
            .IsUnicode(false);

        builder.Property(c => c.TopicCategory)
            .IsRequired()
            .HasMaxLength(WhitelistedChannel.MaxTopicCategoryLength);

        builder.Property(c => c.TrustScore)
            .IsRequired();

        builder.Property(c => c.IsActive)
            .IsRequired();

        builder.Property(c => c.LastSyncedAtUtc);

        builder.Property(c => c.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(c => c.YouTubeChannelId)
            .IsUnique()
            .HasDatabaseName("IX_WhitelistedChannels_YouTubeChannelId");

        builder.HasIndex(c => new { c.IsActive, c.Language })
            .HasDatabaseName("IX_WhitelistedChannels_IsActive_Language");
    }
}
