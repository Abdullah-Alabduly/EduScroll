using EduScroll.Domain.Entities;
using EduScroll.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduScroll.Infrastructure.Persistence.Configurations;

public sealed class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Sources");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .ValueGeneratedNever();

        builder.Property(s => s.OwnerUserId)
            .IsRequired();

        builder.Property(s => s.Title)
            .IsRequired()
            .HasMaxLength(Source.MaxTitleLength);

        builder.Property(s => s.SourceType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(s => s.OriginalUrl)
            .HasMaxLength(Source.MaxOriginalUrlLength);

        builder.Property(s => s.OriginalFileName)
            .HasMaxLength(Source.MaxOriginalFileNameLength);

        // Large UTF-16 text column; intentionally NOT indexed.
        builder.Property(s => s.ExtractedText)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(s => s.ContentLength)
            .IsRequired();

        builder.Property(s => s.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(s => s.CreatedAtUtc)
            .IsRequired();

        builder.Property(s => s.UpdatedAtUtc)
            .IsRequired();

        // Supports efficient owner-scoped pagination sorted newest-first without indexing ExtractedText.
        builder.HasIndex(s => new { s.OwnerUserId, s.CreatedAtUtc })
            .IsDescending(false, true)
            .HasDatabaseName("IX_Sources_OwnerUserId_CreatedAtUtc");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(s => s.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
