using EduScroll.Domain.Exceptions;

namespace EduScroll.Domain.Entities;

/// <summary>
/// Represents a normalized educational topic or subtopic extracted for a <see cref="SourceAnalysis"/>.
/// </summary>
public sealed class SourceTopic
{
    public const int MaxNameLength = 120;

    private SourceTopic()
    {
    }

    public Guid Id { get; private set; }

    public Guid SourceAnalysisId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public Guid? ParentTopicId { get; private set; }

    public int DisplayOrder { get; private set; }

    internal static SourceTopic Create(
        Guid sourceAnalysisId,
        string name,
        Guid? parentTopicId,
        int displayOrder)
    {
        if (sourceAnalysisId == Guid.Empty)
        {
            throw new InvalidSourceAnalysisDomainException(
                "SOURCE_TOPIC_INVALID_ANALYSIS",
                "Source topic must belong to a valid analysis.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidSourceAnalysisDomainException(
                "SOURCE_TOPIC_NAME_REQUIRED",
                "Topic name must not be empty.");
        }

        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
        {
            throw new InvalidSourceAnalysisDomainException(
                "SOURCE_TOPIC_NAME_TOO_LONG",
                $"Topic name must not exceed {MaxNameLength} characters.");
        }

        if (displayOrder < 0)
        {
            throw new InvalidSourceAnalysisDomainException(
                "SOURCE_TOPIC_INVALID_ORDER",
                "Topic display order must be non-negative.");
        }

        return new SourceTopic
        {
            Id = Guid.NewGuid(),
            SourceAnalysisId = sourceAnalysisId,
            Name = trimmed,
            ParentTopicId = parentTopicId,
            DisplayOrder = displayOrder
        };
    }
}
