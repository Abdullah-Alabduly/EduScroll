using System.Text.Json;
using EduScroll.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EduScroll.Infrastructure.Persistence.Configurations;

public sealed class SourceAnalysisConfiguration : IEntityTypeConfiguration<SourceAnalysis>
{
    private static readonly ValueConverter<IReadOnlyList<string>, string> StringListConverter = new(
        list => JsonSerializer.Serialize(list, (JsonSerializerOptions?)null),
        json => DeserializeStringList(json));

    private static readonly ValueComparer<IReadOnlyList<string>> StringListComparer = new(
        (left, right) =>
            ReferenceEquals(left, right) ||
            (left != null && right != null && left.SequenceEqual(right, StringComparer.Ordinal)),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, StringComparer.Ordinal.GetHashCode(item))),
        list => list.ToArray());

    public void Configure(EntityTypeBuilder<SourceAnalysis> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SourceAnalyses");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .ValueGeneratedNever();

        builder.Property(a => a.SourceId)
            .IsRequired();

        builder.Property(a => a.OwnerUserId)
            .IsRequired();

        builder.Property(a => a.TargetFeedLanguage)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasDefaultValue(TargetFeedLanguage.En);

        builder.Property(a => a.Subject)
            .IsRequired()
            .HasMaxLength(SourceAnalysis.MaxSubjectLength);

        builder.Property(a => a.Summary)
            .IsRequired()
            .HasMaxLength(SourceAnalysis.MaxSummaryLength);

        builder.Property(a => a.Concepts)
            .IsRequired()
            .HasColumnName("ConceptsJson")
            .HasColumnType("nvarchar(max)")
            .HasConversion(StringListConverter, StringListComparer);

        builder.Property(a => a.Keywords)
            .IsRequired()
            .HasColumnName("KeywordsJson")
            .HasColumnType("nvarchar(max)")
            .HasConversion(StringListConverter, StringListComparer);

        builder.Property(a => a.ProviderName)
            .IsRequired()
            .HasMaxLength(SourceAnalysis.MaxProviderNameLength);

        builder.Property(a => a.ModelName)
            .IsRequired()
            .HasMaxLength(SourceAnalysis.MaxModelNameLength);

        builder.Property(a => a.PromptVersion)
            .IsRequired()
            .HasMaxLength(SourceAnalysis.MaxPromptVersionLength);

        builder.Property(a => a.AnalyzedAtUtc)
            .IsRequired();

        builder.Property(a => a.UpdatedAtUtc)
            .IsRequired();

        // 1:1 with Source; deleting a Source cascades cleanly to its SourceAnalysis and child tables.
        builder.HasIndex(a => a.SourceId)
            .IsUnique()
            .HasDatabaseName("IX_SourceAnalyses_SourceId");

        builder.HasIndex(a => new { a.OwnerUserId, a.SourceId })
            .HasDatabaseName("IX_SourceAnalyses_OwnerUserId_SourceId");

        builder.HasOne<Source>()
            .WithOne()
            .HasForeignKey<SourceAnalysis>(a => a.SourceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.Topics)
            .WithOne()
            .HasForeignKey(t => t.SourceAnalysisId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.SearchQueries)
            .WithOne()
            .HasForeignKey(q => q.SourceAnalysisId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(a => a.Topics)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(a => a.SearchQueries)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static IReadOnlyList<string> DeserializeStringList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<string[]>(json) ?? [];
    }
}
