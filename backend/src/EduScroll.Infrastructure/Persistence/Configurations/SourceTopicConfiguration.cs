using EduScroll.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduScroll.Infrastructure.Persistence.Configurations;

public sealed class SourceTopicConfiguration : IEntityTypeConfiguration<SourceTopic>
{
    public void Configure(EntityTypeBuilder<SourceTopic> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SourceTopics");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .ValueGeneratedNever();

        builder.Property(t => t.SourceAnalysisId)
            .IsRequired();

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(SourceTopic.MaxNameLength);

        // Logical parent topic reference within the same SourceAnalysis aggregate;
        // kept without a self-referencing NO ACTION FK so SQL Server ON DELETE CASCADE
        // from Sources -> SourceAnalyses -> SourceTopics executes cleanly.
        builder.Property(t => t.ParentTopicId);

        builder.Property(t => t.DisplayOrder)
            .IsRequired();

        builder.HasIndex(t => new { t.SourceAnalysisId, t.DisplayOrder })
            .HasDatabaseName("IX_SourceTopics_SourceAnalysisId_DisplayOrder");
    }
}
