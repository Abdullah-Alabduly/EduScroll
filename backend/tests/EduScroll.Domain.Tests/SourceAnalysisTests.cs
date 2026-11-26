using EduScroll.Domain.Entities;
using EduScroll.Domain.Exceptions;

namespace EduScroll.Domain.Tests;

public sealed class SourceAnalysisTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 26, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidDualLanguageInputs_ShouldPopulateEntitiesTopicsAndTaggedQueries()
    {
        var sourceId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        var analysis = SourceAnalysis.Create(
            sourceId: sourceId,
            ownerUserId: ownerId,
            targetFeedLanguage: TargetFeedLanguage.Both,
            subject: "  Biology  ",
            summary: "  Photosynthesis converts light energy into chemical energy inside plant chloroplasts.  ",
            topics:
            [
                ("Photosynthesis", ["Light Reactions", "Calvin Cycle", "light reactions", "Photosynthesis"]),
                ("Cellular Energy", ["ATP Synthesis"])
            ],
            concepts: ["Chlorophyll", "ATP", "chlorophyll"],
            keywords: ["photosynthesis", "chloroplast", "ATP", "Chloroplast"],
            searchQueries:
            [
                ("how photosynthesis works", "en"),
                ("calvin cycle explained", "en"),
                ("HOW PHOTOSYNTHESIS WORKS", "EN"),
                ("شرح عملية البناء الضوئي", "ar"),
                ("دورة كالفن في النبات", "ar")
            ],
            providerName: "Ollama",
            modelName: "llama3.2",
            promptVersion: "v1.1",
            utcNow: FixedNow);

        Assert.NotEqual(Guid.Empty, analysis.Id);
        Assert.Equal(sourceId, analysis.SourceId);
        Assert.Equal(ownerId, analysis.OwnerUserId);
        Assert.Equal(TargetFeedLanguage.Both, analysis.TargetFeedLanguage);
        Assert.Equal("Biology", analysis.Subject);
        Assert.Equal("Ollama", analysis.ProviderName);
        Assert.Equal("llama3.2", analysis.ModelName);
        Assert.Equal("v1.1", analysis.PromptVersion);
        Assert.Equal(FixedNow, analysis.AnalyzedAtUtc);
        Assert.True(analysis.IsOwnedBy(ownerId));
        Assert.False(analysis.IsOwnedBy(Guid.NewGuid()));
        Assert.False(analysis.IsOwnedBy(Guid.Empty));

        Assert.Equal(5, analysis.Topics.Count);
        Assert.Equal(2, analysis.Topics.Count(t => t.ParentTopicId is null));
        Assert.Equal(3, analysis.Topics.Count(t => t.ParentTopicId is not null));

        Assert.Equal(["Chlorophyll", "ATP"], analysis.Concepts);
        Assert.Equal(["photosynthesis", "chloroplast", "ATP"], analysis.Keywords);
        Assert.Equal(4, analysis.SearchQueries.Count);
        Assert.Equal(2, analysis.SearchQueries.Count(q => q.Language == "en"));
        Assert.Equal(2, analysis.SearchQueries.Count(q => q.Language == "ar"));
    }

    [Fact]
    public void Create_WithMismatchedQueryLanguage_ShouldThrow()
    {
        var ex = Assert.Throws<InvalidSourceAnalysisDomainException>(() =>
            SourceAnalysis.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                TargetFeedLanguage.En,
                "Physics",
                "Newton's laws describe the relationship between motion and force.",
                [("Mechanics", [])],
                ["Inertia"],
                ["force"],
                [("قوانين نيوتن للحركة", "ar")],
                "Ollama",
                "llama3.2",
                "v1.1",
                FixedNow));

        Assert.Equal("SOURCE_QUERY_LANGUAGE_MISMATCH", ex.ErrorCode);
    }

    [Fact]
    public void Create_WithUnsupportedQueryLanguage_ShouldThrow()
    {
        var ex = Assert.Throws<InvalidSourceAnalysisDomainException>(() =>
            SourceAnalysis.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                TargetFeedLanguage.En,
                "Physics",
                "Newton's laws describe the relationship between motion and force.",
                [("Mechanics", [])],
                ["Inertia"],
                ["force"],
                [("lois de newton", "fr")],
                "Ollama",
                "llama3.2",
                "v1.1",
                FixedNow));

        Assert.Equal("SOURCE_QUERY_INVALID_LANGUAGE", ex.ErrorCode);
    }

    [Fact]
    public void Create_WithEmptyOwner_ShouldThrow()
    {
        var ex = Assert.Throws<InvalidSourceAnalysisDomainException>(() =>
            SourceAnalysis.Create(
                Guid.NewGuid(),
                Guid.Empty,
                TargetFeedLanguage.En,
                "Physics",
                "Newton's laws describe the relationship between motion and force.",
                [("Mechanics", [])],
                ["Inertia"],
                ["force"],
                [("newton laws explained", "en")],
                "Ollama",
                "llama3.2",
                "v1.1",
                FixedNow));

        Assert.Equal("SOURCE_ANALYSIS_INVALID_OWNER", ex.ErrorCode);
    }

    [Fact]
    public void Create_WithMissingTopicsOrQueries_ShouldThrow()
    {
        Assert.Throws<InvalidSourceAnalysisDomainException>(() =>
            SourceAnalysis.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                TargetFeedLanguage.En,
                "Physics",
                "Newton's laws describe the relationship between motion and force.",
                [],
                ["Inertia"],
                ["force"],
                [("newton laws explained", "en")],
                "Ollama",
                "llama3.2",
                "v1.1",
                FixedNow));

        Assert.Throws<InvalidSourceAnalysisDomainException>(() =>
            SourceAnalysis.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                TargetFeedLanguage.En,
                "Physics",
                "Newton's laws describe the relationship between motion and force.",
                [("Mechanics", [])],
                ["Inertia"],
                ["force"],
                [],
                "Ollama",
                "llama3.2",
                "v1.1",
                FixedNow));
    }
}
