using System.Text.RegularExpressions;
using EduScroll.Application.AiAnalysis.Contracts;
using EduScroll.Application.AiAnalysis.Models;
using EduScroll.Application.AiAnalysis.Ports;
using EduScroll.Application.Exceptions;
using EduScroll.Application.Sources.Ports;
using EduScroll.Application.Sources.Services;
using EduScroll.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EduScroll.Application.AiAnalysis.Services;

public sealed partial class SourceAnalysisService(
    ISourceRepository sourceRepository,
    ISourceAnalysisRepository analysisRepository,
    IAiAnalysisProvider aiProvider,
    AiAnalysisLimits limits,
    TimeProvider timeProvider,
    ILogger<SourceAnalysisService> logger) : ISourceAnalysisService
{
    private const int MaxLanguageHintLength = 32;

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9\-\s]{0,31}$", RegexOptions.Compiled)]
    private static partial Regex LanguageHintRegex();

    public async Task<SourceAnalysisDto> AnalyzeSourceAsync(
        Guid ownerUserId,
        Guid sourceId,
        AnalyzeSourceRequest? request,
        CancellationToken cancellationToken)
    {
        EnsureValidOwner(ownerUserId);

        if (sourceId == Guid.Empty)
        {
            throw new NotFoundAppException("The requested source was not found.", "SOURCE_NOT_FOUND");
        }

        var effectiveRequest = request ?? new AnalyzeSourceRequest();
        var languageHint = ValidateLanguageHint(effectiveRequest.LanguageHint);
        var targetFeedLanguage = ParseTargetFeedLanguage(effectiveRequest.TargetFeedLanguage);

        // Enforce strict user ownership via Sprint 4 repository before invoking any AI provider.
        var source = await sourceRepository.GetByIdForOwnerAsync(sourceId, ownerUserId, cancellationToken);
        if (source is null || !source.IsOwnedBy(ownerUserId))
        {
            throw new NotFoundAppException("The requested source was not found.", "SOURCE_NOT_FOUND");
        }

        // Reuse cached analysis ONLY when forceRefresh is false AND TargetFeedLanguage matches.
        if (!effectiveRequest.ForceRefresh)
        {
            var existing = await analysisRepository.GetBySourceIdForOwnerAsync(
                sourceId,
                ownerUserId,
                cancellationToken);
            if (existing is not null &&
                existing.IsOwnedBy(ownerUserId) &&
                existing.TargetFeedLanguage == targetFeedLanguage)
            {
                logger.LogInformation(
                    "Returning existing source analysis. SourceId={SourceId} OwnerUserId={OwnerUserId} AnalysisId={AnalysisId} TargetFeedLanguage={TargetFeedLanguage}",
                    sourceId,
                    ownerUserId,
                    existing.Id,
                    targetFeedLanguage);

                return MapToDto(existing);
            }
        }

        // Stage 1 & 2 (ai-pipeline.md): Validate & normalize extracted source text before AI invocation.
        var cleanedText = SourceTextNormalizer.Normalize(source.ExtractedText);
        if (string.IsNullOrWhiteSpace(cleanedText) || cleanedText.Length < limits.MinInputCharacters)
        {
            throw new ValidationAppException(
                $"Source text must contain at least {limits.MinInputCharacters} readable characters for AI analysis.",
                new Dictionary<string, string[]>
                {
                    ["extractedText"] = [$"At least {limits.MinInputCharacters} characters are required for analysis."]
                },
                "AI_INPUT_TOO_SHORT");
        }

        if (cleanedText.Length > limits.MaxInputCharacters)
        {
            throw new UnprocessableEntityAppException(
                $"Source text length ({cleanedText.Length:N0} characters) exceeds the maximum AI analysis limit of {limits.MaxInputCharacters:N0} characters.",
                "AI_INPUT_TOO_LONG");
        }

        // Stage 3–6: Invoke provider abstraction + strict output validation with bounded repair attempts.
        var maxAttempts = Math.Max(1, 1 + limits.MaxSchemaRepairAttempts);
        string? repairFeedback = null;
        AiAnalysisProviderResponse? providerResponse = null;
        ValidatedAiAnalysisOutput? validatedOutput = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var providerRequest = new AiAnalysisProviderRequest(
                SourceTitle: source.Title,
                CleanedText: cleanedText,
                TargetFeedLanguage: targetFeedLanguage,
                LanguageHint: languageHint,
                MaxTopics: limits.MaxTopics,
                MaxSubtopicsPerTopic: limits.MaxSubtopicsPerTopic,
                MaxConcepts: limits.MaxConcepts,
                MaxKeywords: limits.MaxKeywords,
                SingleLanguageMinSearchQueries: limits.SingleLanguageMinSearchQueries,
                SingleLanguageMaxSearchQueries: limits.SingleLanguageMaxSearchQueries,
                DualLanguageMinSearchQueriesPerLanguage: limits.DualLanguageMinSearchQueriesPerLanguage,
                DualLanguageMaxSearchQueriesPerLanguage: limits.DualLanguageMaxSearchQueriesPerLanguage,
                RepairFeedback: repairFeedback);

            providerResponse = await aiProvider.AnalyzeAsync(providerRequest, cancellationToken);
            var validation = AiAnalysisOutputValidator.Validate(
                providerResponse.RawJson,
                targetFeedLanguage,
                limits);

            if (validation.IsValid && validation.Output is not null)
            {
                validatedOutput = validation.Output;
                break;
            }

            logger.LogWarning(
                "AI analysis output failed schema validation. SourceId={SourceId} OwnerUserId={OwnerUserId} TargetFeedLanguage={TargetFeedLanguage} Attempt={Attempt}/{MaxAttempts} ErrorCount={ErrorCount}",
                sourceId,
                ownerUserId,
                targetFeedLanguage,
                attempt,
                maxAttempts,
                validation.Errors.Count);

            repairFeedback = validation.BuildSafeRepairFeedback();
        }

        if (validatedOutput is null || providerResponse is null)
        {
            throw new UnprocessableEntityAppException(
                "The AI provider returned an invalid or incomplete analysis response. Please try again.",
                "AI_SCHEMA_INVALID");
        }

        var now = timeProvider.GetUtcNow();
        var topicTuples = validatedOutput.Topics
            .Select(t => (t.Name, t.Subtopics))
            .ToArray();
        var queryTuples = validatedOutput.SearchQueries
            .Select(q => (q.Query, q.Language))
            .ToArray();

        var analysis = SourceAnalysis.Create(
            sourceId: source.Id,
            ownerUserId: ownerUserId,
            targetFeedLanguage: targetFeedLanguage,
            subject: validatedOutput.Subject,
            summary: validatedOutput.Summary,
            topics: topicTuples,
            concepts: validatedOutput.Concepts,
            keywords: validatedOutput.Keywords,
            searchQueries: queryTuples,
            providerName: providerResponse.ProviderName,
            modelName: providerResponse.ModelName,
            promptVersion: providerResponse.PromptVersion,
            utcNow: now);

        await analysisRepository.SaveOrReplaceAsync(analysis, cancellationToken);

        logger.LogInformation(
            "Source analysis completed and saved. SourceId={SourceId} OwnerUserId={OwnerUserId} AnalysisId={AnalysisId} TargetFeedLanguage={TargetFeedLanguage} Provider={Provider} Model={Model} PromptVersion={PromptVersion} InputLength={InputLength} TopicCount={TopicCount} QueryCount={QueryCount}",
            source.Id,
            ownerUserId,
            analysis.Id,
            analysis.TargetFeedLanguage,
            analysis.ProviderName,
            analysis.ModelName,
            analysis.PromptVersion,
            cleanedText.Length,
            validatedOutput.Topics.Count,
            validatedOutput.SearchQueries.Count);

        return MapToDto(analysis);
    }

    public async Task<SourceAnalysisDto> GetAnalysisBySourceIdAsync(
        Guid ownerUserId,
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        EnsureValidOwner(ownerUserId);

        if (sourceId == Guid.Empty)
        {
            throw new NotFoundAppException("The requested source was not found.", "SOURCE_NOT_FOUND");
        }

        var source = await sourceRepository.GetByIdForOwnerAsync(sourceId, ownerUserId, cancellationToken);
        if (source is null || !source.IsOwnedBy(ownerUserId))
        {
            throw new NotFoundAppException("The requested source was not found.", "SOURCE_NOT_FOUND");
        }

        var analysis = await analysisRepository.GetBySourceIdForOwnerAsync(sourceId, ownerUserId, cancellationToken);
        if (analysis is null || !analysis.IsOwnedBy(ownerUserId))
        {
            throw new NotFoundAppException(
                "No AI analysis exists for this source yet.",
                "SOURCE_ANALYSIS_NOT_FOUND");
        }

        return MapToDto(analysis);
    }

    public static TargetFeedLanguage ParseTargetFeedLanguage(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return TargetFeedLanguage.En;
        }

        return rawValue.Trim().ToLowerInvariant() switch
        {
            "en" => TargetFeedLanguage.En,
            "ar" => TargetFeedLanguage.Ar,
            "both" => TargetFeedLanguage.Both,
            _ => throw new ValidationAppException(
                "Target feed language must be 'en', 'ar', or 'both'.",
                new Dictionary<string, string[]>
                {
                    ["targetFeedLanguage"] = ["Allowed values are 'en', 'ar', and 'both'."]
                },
                "AI_INVALID_TARGET_FEED_LANGUAGE")
        };
    }

    private static string FormatTargetFeedLanguage(TargetFeedLanguage language) =>
        language switch
        {
            TargetFeedLanguage.En => "en",
            TargetFeedLanguage.Ar => "ar",
            TargetFeedLanguage.Both => "both",
            _ => "en"
        };

    private static string? ValidateLanguageHint(string? rawLanguageHint)
    {
        if (string.IsNullOrWhiteSpace(rawLanguageHint))
        {
            return null;
        }

        var trimmed = rawLanguageHint.Trim();
        if (trimmed.Length > MaxLanguageHintLength || !LanguageHintRegex().IsMatch(trimmed))
        {
            throw new ValidationAppException(
                "Language hint format is invalid.",
                new Dictionary<string, string[]>
                {
                    ["languageHint"] = ["Language hint must contain only letters, digits, hyphens, or spaces (max 32 chars)."]
                },
                "AI_INVALID_LANGUAGE_HINT");
        }

        return trimmed;
    }

    private static void EnsureValidOwner(Guid ownerUserId)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new UnauthorizedAppException(
                "Authenticated user identifier is missing or invalid.",
                "UNAUTHORIZED");
        }
    }

    private static SourceAnalysisDto MapToDto(SourceAnalysis analysis)
    {
        var orderedTopics = analysis.Topics.OrderBy(t => t.DisplayOrder).ToArray();
        var topLevel = orderedTopics.Where(t => t.ParentTopicId is null).ToArray();
        var subtopicsByParent = orderedTopics
            .Where(t => t.ParentTopicId.HasValue)
            .GroupBy(t => t.ParentTopicId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.OrderBy(x => x.DisplayOrder).Select(x => x.Name).ToArray());

        var topicDtos = topLevel
            .Select(t => new SourceAnalysisTopicDto(
                t.Name,
                subtopicsByParent.TryGetValue(t.Id, out var subs) ? subs : []))
            .ToArray();

        var primaryTopic = topicDtos.Length > 0 ? topicDtos[0].Name : analysis.Subject;

        var orderedQueries = analysis.SearchQueries
            .OrderBy(q => q.DisplayOrder)
            .Select(q => new SourceSearchQueryDto(q.QueryText, q.Language))
            .ToArray();

        return new SourceAnalysisDto(
            Id: analysis.Id,
            SourceId: analysis.SourceId,
            TargetFeedLanguage: FormatTargetFeedLanguage(analysis.TargetFeedLanguage),
            Subject: analysis.Subject,
            Topic: primaryTopic,
            Summary: analysis.Summary,
            Topics: topicDtos,
            Concepts: analysis.Concepts,
            Keywords: analysis.Keywords,
            SearchQueries: orderedQueries,
            Provider: analysis.ProviderName,
            Model: analysis.ModelName,
            PromptVersion: analysis.PromptVersion,
            AnalyzedAtUtc: analysis.AnalyzedAtUtc,
            UpdatedAtUtc: analysis.UpdatedAtUtc);
    }
}
