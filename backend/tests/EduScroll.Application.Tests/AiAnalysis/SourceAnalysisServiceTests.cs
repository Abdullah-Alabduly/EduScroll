using EduScroll.Application.AiAnalysis.Contracts;
using EduScroll.Application.AiAnalysis.Models;
using EduScroll.Application.AiAnalysis.Ports;
using EduScroll.Application.AiAnalysis.Services;
using EduScroll.Application.Exceptions;
using EduScroll.Application.Sources.Models;
using EduScroll.Application.Sources.Ports;
using EduScroll.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduScroll.Application.Tests.AiAnalysis;

public sealed class SourceAnalysisServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 26, 18, 30, 0, TimeSpan.Zero);

    private static readonly string ValidSourceText =
        "Gradient descent is a first-order iterative optimization algorithm for finding a local minimum of a differentiable loss function by moving opposite the gradient.";

    private static readonly string ValidEnJson = """
        {
          "subject": "Machine Learning",
          "topic": "Gradient Descent",
          "summary": "Gradient descent iteratively updates model parameters in the negative gradient direction to minimize loss.",
          "topics": [
            {
              "name": "Gradient Descent",
              "subtopics": ["Learning Rate", "Loss Minimization"]
            }
          ],
          "concepts": ["Gradient", "Learning Rate", "Loss Function"],
          "keywords": ["gradient descent", "optimization", "machine learning"],
          "searchQueries": [
            { "query": "gradient descent intuition", "language": "en" },
            { "query": "learning rate in gradient descent", "language": "en" },
            { "query": "minimizing loss function machine learning", "language": "en" }
          ]
        }
        """;

    private static readonly string ValidArJson = """
        {
          "subject": "Machine Learning",
          "topic": "Gradient Descent",
          "summary": "Gradient descent iteratively updates model parameters in the negative gradient direction to minimize loss.",
          "topics": [
            {
              "name": "Gradient Descent",
              "subtopics": ["Learning Rate", "Loss Minimization"]
            }
          ],
          "concepts": ["Gradient", "Learning Rate", "Loss Function"],
          "keywords": ["gradient descent", "optimization", "machine learning"],
          "searchQueries": [
            { "query": "شرح خوارزمية الانحدار التدريجي", "language": "ar" },
            { "query": "معدل التعلم في تعلم الآلة", "language": "ar" },
            { "query": "تقليل دالة الخسارة في الشبكات العصبية", "language": "ar" }
          ]
        }
        """;

    private static readonly string ValidBothJson = """
        {
          "subject": "Machine Learning",
          "topic": "Gradient Descent",
          "summary": "Gradient descent iteratively updates model parameters in the negative gradient direction to minimize loss.",
          "topics": [
            {
              "name": "Gradient Descent",
              "subtopics": ["Learning Rate", "Loss Minimization"]
            }
          ],
          "concepts": ["Gradient", "Learning Rate", "Loss Function"],
          "keywords": ["gradient descent", "optimization", "machine learning"],
          "searchQueries": [
            { "query": "gradient descent visual intuition", "language": "en" },
            { "query": "learning rate optimization explained", "language": "en" },
            { "query": "شرح خوارزمية الانحدار التدريجي", "language": "ar" },
            { "query": "معدل التعلم ودالة الخسارة", "language": "ar" }
          ]
        }
        """;

    [Fact]
    public async Task AnalyzeSourceAsync_ShouldDefaultToEn_ReuseMatchingCache_AndReRunWhenTargetLanguageChanges()
    {
        var ownerId = Guid.NewGuid();
        var source = Source.CreatePdf(ownerId, "Optimization Notes", ValidSourceText, FixedNow);
        var sourceRepo = new InMemorySourceRepository(source);
        var analysisRepo = new InMemorySourceAnalysisRepository();
        var provider = new FakeAiAnalysisProvider([ValidEnJson, ValidArJson, ValidBothJson]);

        var service = CreateService(sourceRepo, analysisRepo, provider);

        // 1. Omitted TargetFeedLanguage defaults to "en"
        var enResult = await service.AnalyzeSourceAsync(ownerId, source.Id, null, CancellationToken.None);
        Assert.Equal("en", enResult.TargetFeedLanguage);
        Assert.Equal(3, enResult.SearchQueries.Count);
        Assert.All(enResult.SearchQueries, q => Assert.Equal("en", q.Language));
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(TargetFeedLanguage.En, provider.ReceivedRequests[0].TargetFeedLanguage);

        // 2. Same TargetFeedLanguage ("en") with ForceRefresh=false returns cached analysis
        var cachedEn = await service.AnalyzeSourceAsync(
            ownerId,
            source.Id,
            new AnalyzeSourceRequest(ForceRefresh: false, TargetFeedLanguage: "en"),
            CancellationToken.None);
        Assert.Equal(enResult.Id, cachedEn.Id);
        Assert.Equal(1, provider.CallCount);

        // 3. Different TargetFeedLanguage ("ar") with ForceRefresh=false MUST re-run & replace
        var arResult = await service.AnalyzeSourceAsync(
            ownerId,
            source.Id,
            new AnalyzeSourceRequest(ForceRefresh: false, TargetFeedLanguage: "ar"),
            CancellationToken.None);
        Assert.NotEqual(enResult.Id, arResult.Id);
        Assert.Equal("ar", arResult.TargetFeedLanguage);
        Assert.All(arResult.SearchQueries, q => Assert.Equal("ar", q.Language));
        Assert.Equal(2, provider.CallCount);
        Assert.Equal(TargetFeedLanguage.Ar, provider.ReceivedRequests[1].TargetFeedLanguage);

        // 4. Switching to "both" with ForceRefresh=false MUST re-run & replace
        var bothResult = await service.AnalyzeSourceAsync(
            ownerId,
            source.Id,
            new AnalyzeSourceRequest(ForceRefresh: false, TargetFeedLanguage: "both"),
            CancellationToken.None);
        Assert.NotEqual(arResult.Id, bothResult.Id);
        Assert.Equal("both", bothResult.TargetFeedLanguage);
        Assert.Equal(2, bothResult.SearchQueries.Count(q => q.Language == "en"));
        Assert.Equal(2, bothResult.SearchQueries.Count(q => q.Language == "ar"));
        Assert.Equal(3, provider.CallCount);
        Assert.Single(analysisRepo.Store);
    }

    [Fact]
    public async Task AnalyzeSourceAsync_WithInvalidTargetFeedLanguage_ShouldThrowValidationException()
    {
        var ownerId = Guid.NewGuid();
        var source = Source.CreatePdf(ownerId, "Optimization Notes", ValidSourceText, FixedNow);
        var service = CreateService(
            new InMemorySourceRepository(source),
            new InMemorySourceAnalysisRepository(),
            new FakeAiAnalysisProvider([ValidEnJson]));

        var ex = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.AnalyzeSourceAsync(
                ownerId,
                source.Id,
                new AnalyzeSourceRequest(TargetFeedLanguage: "fr"),
                CancellationToken.None));

        Assert.Equal("AI_INVALID_TARGET_FEED_LANGUAGE", ex.ErrorCode);
    }

    [Fact]
    public async Task AnalyzeSourceAsync_WhenFirstResponseMalformed_ShouldUseBoundedRepairAndSucceed()
    {
        var ownerId = Guid.NewGuid();
        var source = Source.CreatePdf(ownerId, "Optimization Notes", ValidSourceText, FixedNow);
        var sourceRepo = new InMemorySourceRepository(source);
        var analysisRepo = new InMemorySourceAnalysisRepository();
        var provider = new FakeAiAnalysisProvider(["{ malformed json", ValidEnJson]);

        var service = CreateService(sourceRepo, analysisRepo, provider, new AiAnalysisLimits(MaxSchemaRepairAttempts: 1));

        var result = await service.AnalyzeSourceAsync(ownerId, source.Id, null, CancellationToken.None);

        Assert.Equal("Machine Learning", result.Subject);
        Assert.Equal(2, provider.CallCount);
        Assert.Null(provider.ReceivedRequests[0].RepairFeedback);
        Assert.NotNull(provider.ReceivedRequests[1].RepairFeedback);
        Assert.Single(analysisRepo.Store);
    }

    [Fact]
    public async Task AnalyzeSourceAsync_WhenAllRepairAttemptsInvalid_ShouldFailWithAiSchemaInvalidWithoutPersisting()
    {
        var ownerId = Guid.NewGuid();
        var source = Source.CreatePdf(ownerId, "Optimization Notes", ValidSourceText, FixedNow);
        var sourceRepo = new InMemorySourceRepository(source);
        var analysisRepo = new InMemorySourceAnalysisRepository();
        // Return Arabic queries when English was requested -> rejected on all attempts
        var provider = new FakeAiAnalysisProvider([ValidArJson, ValidArJson]);

        var service = CreateService(sourceRepo, analysisRepo, provider, new AiAnalysisLimits(MaxSchemaRepairAttempts: 1));

        var ex = await Assert.ThrowsAsync<UnprocessableEntityAppException>(() =>
            service.AnalyzeSourceAsync(ownerId, source.Id, new AnalyzeSourceRequest(TargetFeedLanguage: "en"), CancellationToken.None));

        Assert.Equal("AI_SCHEMA_INVALID", ex.ErrorCode);
        Assert.Equal(2, provider.CallCount);
        Assert.Empty(analysisRepo.Store);
    }

    [Fact]
    public async Task AnalyzeSourceAsync_WhenSourceTextExceedsMaxInputLimit_ShouldRejectWithoutCallingProvider()
    {
        var ownerId = Guid.NewGuid();
        var oversizedText = new string('A', 2000);
        var source = Source.CreatePdf(ownerId, "Long Book", oversizedText, FixedNow);
        var sourceRepo = new InMemorySourceRepository(source);
        var analysisRepo = new InMemorySourceAnalysisRepository();
        var provider = new FakeAiAnalysisProvider([ValidEnJson]);

        var service = CreateService(
            sourceRepo,
            analysisRepo,
            provider,
            new AiAnalysisLimits(MinInputCharacters: 50, MaxInputCharacters: 500));

        var ex = await Assert.ThrowsAsync<UnprocessableEntityAppException>(() =>
            service.AnalyzeSourceAsync(ownerId, source.Id, null, CancellationToken.None));

        Assert.Equal("AI_INPUT_TOO_LONG", ex.ErrorCode);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task AnalyzeSourceAsync_WhenSourceTextBelowMinInputLimit_ShouldRejectWithoutCallingProvider()
    {
        var ownerId = Guid.NewGuid();
        var source = Source.CreatePdf(ownerId, "Short Note", new string('B', 60), FixedNow);
        var sourceRepo = new InMemorySourceRepository(source);
        var analysisRepo = new InMemorySourceAnalysisRepository();
        var provider = new FakeAiAnalysisProvider([ValidEnJson]);

        var service = CreateService(
            sourceRepo,
            analysisRepo,
            provider,
            new AiAnalysisLimits(MinInputCharacters: 100, MaxInputCharacters: 5000));

        var ex = await Assert.ThrowsAsync<ValidationAppException>(() =>
            service.AnalyzeSourceAsync(ownerId, source.Id, null, CancellationToken.None));

        Assert.Equal("AI_INPUT_TOO_SHORT", ex.ErrorCode);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task CrossUserAccess_UserBCannotAnalyzeOrRetrieveUserASourceAnalysis()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var sourceA = Source.CreatePdf(userA, "Private Notes", ValidSourceText, FixedNow);
        var sourceRepo = new InMemorySourceRepository(sourceA);
        var analysisRepo = new InMemorySourceAnalysisRepository();
        var provider = new FakeAiAnalysisProvider([ValidEnJson]);
        var service = CreateService(sourceRepo, analysisRepo, provider);

        var analyzeEx = await Assert.ThrowsAsync<NotFoundAppException>(() =>
            service.AnalyzeSourceAsync(userB, sourceA.Id, null, CancellationToken.None));
        Assert.Equal("SOURCE_NOT_FOUND", analyzeEx.ErrorCode);
        Assert.Equal(0, provider.CallCount);

        await service.AnalyzeSourceAsync(userA, sourceA.Id, null, CancellationToken.None);

        var getEx = await Assert.ThrowsAsync<NotFoundAppException>(() =>
            service.GetAnalysisBySourceIdAsync(userB, sourceA.Id, CancellationToken.None));
        Assert.Equal("SOURCE_NOT_FOUND", getEx.ErrorCode);
    }

    [Fact]
    public async Task AnalyzeSourceAsync_WhenCancelled_ShouldPropagateOperationCanceledException()
    {
        var ownerId = Guid.NewGuid();
        var source = Source.CreatePdf(ownerId, "Notes", ValidSourceText, FixedNow);
        var sourceRepo = new InMemorySourceRepository(source);
        var analysisRepo = new InMemorySourceAnalysisRepository();
        var provider = new FakeAiAnalysisProvider([ValidEnJson]);
        var service = CreateService(sourceRepo, analysisRepo, provider);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.AnalyzeSourceAsync(ownerId, source.Id, null, cts.Token));
        Assert.Equal(0, provider.CallCount);
    }

    private static SourceAnalysisService CreateService(
        ISourceRepository sourceRepo,
        ISourceAnalysisRepository analysisRepo,
        IAiAnalysisProvider provider,
        AiAnalysisLimits? limits = null) =>
        new(
            sourceRepo,
            analysisRepo,
            provider,
            limits ?? new AiAnalysisLimits(),
            new FixedTimeProvider(FixedNow),
            NullLogger<SourceAnalysisService>.Instance);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeAiAnalysisProvider(IEnumerable<string> responses) : IAiAnalysisProvider
    {
        private readonly Queue<string> _responses = new(responses);

        public int CallCount { get; private set; }
        public List<AiAnalysisProviderRequest> ReceivedRequests { get; } = [];

        public Task<AiAnalysisProviderResponse> AnalyzeAsync(
            AiAnalysisProviderRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            ReceivedRequests.Add(request);
            var raw = _responses.Count > 0 ? _responses.Dequeue() : string.Empty;
            return Task.FromResult(new AiAnalysisProviderResponse(raw, "Ollama", "llama3.2", "v1.1"));
        }
    }

    private sealed class InMemorySourceRepository(params Source[] sources) : ISourceRepository
    {
        private readonly List<Source> _sources = [.. sources];

        public Task AddAsync(Source source, CancellationToken cancellationToken)
        {
            _sources.Add(source);
            return Task.CompletedTask;
        }

        public Task<Source?> GetByIdForOwnerAsync(Guid sourceId, Guid ownerUserId, CancellationToken cancellationToken)
            => Task.FromResult(_sources.FirstOrDefault(s => s.Id == sourceId && s.OwnerUserId == ownerUserId));

        public Task<(IReadOnlyList<SourceListItem> Items, int TotalCount)> GetPagedForOwnerAsync(
            Guid ownerUserId, int page, int pageSize, int previewMaxLength, CancellationToken cancellationToken)
            => Task.FromResult<(IReadOnlyList<SourceListItem>, int)>(([], 0));

        public Task<bool> DeleteForOwnerAsync(Guid sourceId, Guid ownerUserId, CancellationToken cancellationToken)
            => Task.FromResult(false);
    }

    private sealed class InMemorySourceAnalysisRepository : ISourceAnalysisRepository
    {
        public Dictionary<Guid, SourceAnalysis> Store { get; } = [];

        public Task<SourceAnalysis?> GetBySourceIdForOwnerAsync(
            Guid sourceId,
            Guid ownerUserId,
            CancellationToken cancellationToken)
        {
            if (Store.TryGetValue(sourceId, out var found) && found.OwnerUserId == ownerUserId)
            {
                return Task.FromResult<SourceAnalysis?>(found);
            }

            return Task.FromResult<SourceAnalysis?>(null);
        }

        public Task SaveOrReplaceAsync(SourceAnalysis analysis, CancellationToken cancellationToken)
        {
            Store[analysis.SourceId] = analysis;
            return Task.CompletedTask;
        }
    }
}
