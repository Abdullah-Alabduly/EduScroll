using EduScroll.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduScroll.Infrastructure.Persistence.Configurations;

public sealed class SourceSearchQueryConfiguration : IEntityTypeConfiguration<SourceSearchQuery>
{
    public void Configure(EntityTypeBuilder<SourceSearchQuery> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SourceSearchQueries");

        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id)
            .ValueGeneratedNever();

        builder.Property(q => q.SourceAnalysisId)
            .IsRequired();

        builder.Property(q => q.QueryText)
            .IsRequired()
            .HasMaxLength(SourceSearchQuery.MaxQueryTextLength);

        builder.Property(q => q.Language)
            .IsRequired()
            .HasMaxLength(SourceSearchQuery.MaxLanguageLength)
            .IsUnicode(false)
            .HasDefaultValue(SourceSearchQuery.EnglishCode);

        builder.Property(q => q.DisplayOrder)
            .IsRequired();

        builder.HasIndex(q => new { q.SourceAnalysisId, q.DisplayOrder })
            .HasDatabaseName("IX_SourceSearchQueries_SourceAnalysisId_DisplayOrder");
    }
}
