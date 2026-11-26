# EduScroll — Sprint 5 Completion Report
## AI Content Analysis

- **Project:** EduScroll (Educational Short-Video Discovery Platform)
- **Sprint:** 5 — AI Content Analysis
- **Branch:** `feature/sprint-5-ai-content-analysis` (branched from `feature/sprint-4-source-ingestion`)
- **Date:** 2026-11-26
- **Backend Stack:** ASP.NET Core 10 Web API (`backend/`), Clean Architecture, EF Core 10, Microsoft SQL Server, ASP.NET Core Identity + JWT Bearer, Ollama HTTP Adapter (`ADR-003`)

---

## 1. Executive Summary

Sprint 5 implemented EduScroll's **AI Content Analysis** backend subsystem (including **Multi-Language Feed Query Support** for `en`, `ar`, and `both`), transforming validated extracted text from user-owned Sprint 4 `Source` records into strictly validated, structured educational metadata (`targetFeedLanguage`, `subject`, `topic`, `summary`, hierarchical `topics`/`subtopics`, `concepts`, `keywords`, and language-tagged `searchQueries: [{ "query", "language" }]`) persisted in Microsoft SQL Server.

All architectural, security, and scope boundaries were preserved:
- **Provider-agnostic Application Port (`IAiAnalysisProvider`):** Application logic has zero coupling to Ollama HTTP DTOs, prompt wording, or credentials.
- **Approved Local Provider (`ADR-003`):** Implemented `OllamaAiAnalysisProvider` against Ollama's `/api/chat` structured JSON endpoint (`format: "json"`, `stream: false`) with configurable `AiProviderOptions` + startup `AiProviderOptionsValidator`, per-attempt timeouts, `CancellationToken` propagation, bounded exponential-backoff retries on transient failures, and redacted metadata-only logging.
- **Prompt Injection & Untrusted Output Defense (`SEC-009`, `SEC-010`, `BR-005`):** Untrusted source text is isolated inside `<UNTRUSTED_SOURCE_TEXT>` delimiters (with delimiter escaping), and all AI responses pass strict JSON/schema/HTML/injection validation (`AiAnalysisOutputValidator`) with bounded schema-repair retries—unvalidated output is never persisted.
- **Strict User Ownership Isolation (`SEC-003`):** Every analyze/retrieve request authenticates via JWT Bearer (`User.GetRequiredUserId()`), verifies ownership via Sprint 4's `ISourceRepository`, and denies cross-user access with `404 SOURCE_NOT_FOUND` without invoking the AI provider.
- **Scope Discipline:** **Sprint 6 (YouTube Discovery), Sprint 7 (Video Ranking), Sprint 8 (Full Pipeline Orchestration), and Sprint 9 (Flutter Feed UI) were NOT started.**

---

## 2. Pre-Coding Audit Alignment

- **`docs/03-architecture/adr/ADR-003-ai-provider-deployment.md`:** Confirmed **Ollama** as the initial local AI provider behind `IAiAnalysisProvider`, keeping vendor details isolated in Infrastructure so hosted providers can be swapped in later without touching Domain or Application.
- **`docs/04-ai/ai-pipeline.md`:** Aligned with Stage 1–6 (input text validation/cleaning -> `IAiAnalysisProvider` -> structured JSON generation -> strict untrusted output validation with bounded repair -> sanitized `searchQueries`), `AiProviderOptions` configuration (`CONF-003`), versioned prompts (`PromptVersion`), and `SEC-009`/`SEC-010`.
- **`docs/06-data/conceptual-data-model.md`:** Confirmed `SourceAnalysis`, `Topic`, and `SearchQuery` as required persistent entities tied `1:1` / `N:1` to `Source` with cascade deletion and SQL Server ownership isolation.
- **`CleanArchitectureDependencyTests`:** Verified `EduScroll.Domain` retains zero framework/JSON/HTTP references and `EduScroll.Application` references only `EduScroll.Domain`.

---

## 3. API Contract

All endpoints live in `backend/src/EduScroll.API/Controllers/SourceAnalysisController.cs`, require `[Authorize]` (JWT Bearer), and return RFC 9457 `ProblemDetails` (`application/problem+json`) on errors.

### `POST /api/v1/sources/{id}/analyze`
Triggers AI content analysis for a user-owned source (or returns the existing persisted analysis unless `forceRefresh` is `true` **or** the requested `targetFeedLanguage` differs from the persisted analysis's `targetFeedLanguage`).

- **Optional Request Body (`AnalyzeSourceRequest`, max `4 KB`):**
  ```json
  {
    "forceRefresh": false,
    "languageHint": "en",
    "targetFeedLanguage": "en"
  }
  ```
  - `targetFeedLanguage`: `"en"` (default when omitted), `"ar"`, or `"both"` (case-insensitive).
- **Success (`200 OK`, `SourceAnalysisDto`):**
  ```json
  {
    "id": "b7b0e8d2-...",
    "sourceId": "2d3f4a1c-...",
    "targetFeedLanguage": "both",
    "subject": "Cell Biology",
    "topic": "Mitosis",
    "summary": "Mitosis divides a eukaryotic cell's duplicated chromosomes equally into two genetically identical daughter cells through four ordered phases.",
    "topics": [
      {
        "name": "Mitosis",
        "subtopics": ["Prophase", "Metaphase", "Anaphase", "Telophase"]
      }
    ],
    "concepts": ["Chromosome Segregation", "Daughter Cells", "Spindle Fibers"],
    "keywords": ["mitosis", "cell division", "prophase", "metaphase"],
    "searchQueries": [
      { "query": "mitosis phases animated", "language": "en" },
      { "query": "how mitosis produces identical cells", "language": "en" },
      { "query": "شرح مراحل الانقسام المتساوي للخلية", "language": "ar" },
      { "query": "الطور التمهيدي والاستوائي في الانقسام المتساوي", "language": "ar" }
    ],
    "provider": "Ollama",
    "model": "llama3.2",
    "promptVersion": "v1.1",
    "analyzedAtUtc": "2026-11-26T18:30:00Z",
    "updatedAtUtc": "2026-11-26T18:30:00Z"
  }
  ```
- **Error Mapping:**
  - `400 Bad Request` — `AI_INPUT_TOO_SHORT`, `AI_INVALID_LANGUAGE_HINT`, `AI_INVALID_TARGET_FEED_LANGUAGE`
  - `401 Unauthorized` — Missing/invalid JWT
  - `404 Not Found` — `SOURCE_NOT_FOUND` (missing source or owned by another user)
  - `422 Unprocessable Entity` — `AI_INPUT_TOO_LONG`, `AI_SCHEMA_INVALID` (including missing/mismatched query language tags or out-of-bounds query counts)
  - `499 Client Closed Request` — Caller cancelled request
  - `502 Bad Gateway` — `AI_PROVIDER_UNAVAILABLE`, `AI_PROVIDER_AUTH_FAILED`, `AI_PROVIDER_RATE_LIMITED`
  - `504 Gateway Timeout` — `AI_PROVIDER_TIMEOUT`

### `GET /api/v1/sources/{id}/analysis`
Retrieves the persisted validated AI content analysis for a user-owned source.

- **Success (`200 OK`):** `SourceAnalysisDto`
- **Errors:** `401 Unauthorized`, `404 Not Found` (`SOURCE_NOT_FOUND` if missing/not owned; `SOURCE_ANALYSIS_NOT_FOUND` if not yet analyzed)

---

## 4. Configuration Keys (`AiProvider`)

Bound via `AiProviderOptions` and validated at startup by `AiProviderOptionsValidator` (placeholder `ApiKey` values starting with/containing `REPLACE_WITH_` are rejected at startup). No secrets are committed; `appsettings.Example.json` contains placeholder guidance only.

| Key | Default | Validation / Purpose |
|---|---|---|
| `AiProvider:Provider` | `Ollama` | Supported provider (`Ollama`) |
| `AiProvider:BaseUrl` | `http://localhost:11434` | Valid absolute HTTP/HTTPS endpoint |
| `AiProvider:Model` | `llama3.2` | Required model name (max 128 chars) |
| `AiProvider:ApiKey` | `""` | Optional Bearer key via User Secrets / `AiProvider__ApiKey` (never logged) |
| `AiProvider:TimeoutSeconds` | `60` | Per-attempt timeout (`1..300` sec) |
| `AiProvider:MaxRetries` | `2` | Bounded transient retries (`0..5`) |
| `AiProvider:RetryBaseDelayMilliseconds` | `500` | Exponential backoff base delay (`0..10000` ms) |
| `AiProvider:MinInputCharacters` | `50` | Minimum readable chars required for analysis |
| `AiProvider:MaxInputCharacters` | `24000` | Maximum input chars sent to AI (`<= 200000`) |
| `AiProvider:MaxSchemaRepairAttempts` | `1` | Bounded repair re-prompt attempts on invalid AI JSON (`0..2`) |
| `AiProvider:MaxSummaryLength` | `1200` | Summary ceiling (`50..2000` chars) |
| `AiProvider:MaxTopics` / `MaxSubtopicsPerTopic` | `5` / `4` | Topic/subtopic array bounds |
| `AiProvider:MaxConcepts` / `MaxKeywords` | `10` / `12` | Output concept & keyword list caps |
| `AiProvider:SingleLanguageMinSearchQueries` / `Max` | `3` / `8` | Required query count bounds for `en` or `ar` (`TargetFeedLanguage.En` / `Ar`) |
| `AiProvider:DualLanguageMinSearchQueriesPerLanguage` / `Max` | `2` / `5` | Required per-language query count bounds for `both` (`4–10` total) |
| `AiProvider:Temperature` | `0.1` | Near-deterministic decoding (`0.0..1.0`) |
| `AiProvider:PromptVersion` | `v1.1` | Versioned prompt tag persisted with each analysis |

---

## 5. SQL Server Persistence & Migrations

- **Migrations Created:**
  1. `backend/src/EduScroll.Infrastructure/Persistence/Migrations/20261126182928_AddSourceAnalysisTables.cs`
  2. `backend/src/EduScroll.Infrastructure/Persistence/Migrations/20261126184956_AddMultiLanguageFeedQueries.cs` (adds `TargetFeedLanguage` `nvarchar(16)` default `'En'` to `SourceAnalyses`, and `Language` `varchar(8)` default `'en'` to `SourceSearchQueries`)
- **Tables Added/Updated (Microsoft SQL Server, preserving existing Identity, `RefreshTokens`, and `Sources` tables):**
  - `SourceAnalyses` (`Id` PK, `SourceId` unique FK -> `Sources.Id` `ON DELETE CASCADE`, `OwnerUserId`, `TargetFeedLanguage`, `Subject`, `Summary`, `ConceptsJson`, `KeywordsJson`, `ProviderName`, `ModelName`, `PromptVersion`, `AnalyzedAtUtc`, `UpdatedAtUtc`; indexes `IX_SourceAnalyses_SourceId` unique and `IX_SourceAnalyses_OwnerUserId_SourceId`).
  - `SourceTopics` (`Id` PK, `SourceAnalysisId` FK -> `SourceAnalyses.Id` `ON DELETE CASCADE`, `Name`, `ParentTopicId` nullable, `DisplayOrder`; index `IX_SourceTopics_SourceAnalysisId_DisplayOrder`).
  - `SourceSearchQueries` (`Id` PK, `SourceAnalysisId` FK -> `SourceAnalyses.Id` `ON DELETE CASCADE`, `QueryText`, `Language`, `DisplayOrder`; index `IX_SourceSearchQueries_SourceAnalysisId_DisplayOrder`).
- **Database Verification:** Both migrations applied cleanly to `EduScroll_Dev` on `(localdb)\MSSQLLocalDB` (`ExistingUsers = 1` preserved) and verified end-to-end (including language-change cache replacement and cascade delete on parent `Source` removal) in `DatabaseMigrationAndReadinessTests` and `SourceAnalysisEndpointsIntegrationTests`.

---

## 6. Verification & Test Results

All automated tests are 100% deterministic and use mocked HTTP transports/fakes (zero live AI API calls).

### Commands Executed
```powershell
dotnet build backend/EduScroll.sln
# (from backend/)
dotnet ef database update --project src/EduScroll.Infrastructure/EduScroll.Infrastructure.csproj --startup-project src/EduScroll.API/EduScroll.API.csproj --no-build
dotnet test backend/EduScroll.sln --no-build
```

### Exact Results
- **Build (`dotnet build backend/EduScroll.sln`):** **PASS** — `0 Warning(s), 0 Error(s)` (exit `0`).
- **Migration (`dotnet ef database update`):** **PASS** — `Applying migration '20261126184956_AddMultiLanguageFeedQueries'. Done.` (exit `0`).
- **Automated Test Suite (`dotnet test backend/EduScroll.sln --no-build`):** **248 / 248 Passed** (`0` Failed, `0` Skipped, exit `0`):
  - `EduScroll.Architecture.Tests`: **5 / 5 Passed** (`737 ms`)
  - `EduScroll.Domain.Tests`: **32 / 32 Passed** (`89 ms`)
  - `EduScroll.Application.Tests`: **76 / 76 Passed** (`102 ms`)
  - `EduScroll.API.IntegrationTests`: **135 / 135 Passed** (`6 s`)

### Sprint 5 Test Coverage Highlights
- Valid structured JSON output for `en`, `ar`, and `both` (`{ "query", "language" }` shape) + SQL Server persistence + idempotent same-language cache hit + automatic re-run & atomic replacement when `targetFeedLanguage` changes even with `forceRefresh: false` + cascade deletion on parent `Source` removal.
- Strict query count bounds (`3–8` single language, `2–5` per language / `4–10` total for `both`) and rejection of untagged, unsupported (`"fr"`), or mismatched language tags (`422 AI_SCHEMA_INVALID`) with nothing persisted.
- Invalid `targetFeedLanguage` request parameter rejected (`400 AI_INVALID_TARGET_FEED_LANGUAGE`).
- Bounded markdown-fence JSON extraction and bounded schema-repair re-prompting.
- HTML/script/prompt-injection output rejected by `AiAnalysisOutputValidator` and `<UNTRUSTED_SOURCE_TEXT>` delimiter escaping + translation/adaptation prompt instructions verified in `OllamaAiAnalysisProviderTests`.
- Empty/too-short (`400 AI_INPUT_TOO_SHORT`) and oversized (`422 AI_INPUT_TOO_LONG`) source text rejected without calling the AI provider.
- Transient `503` retry success, non-transient `401` fail-fast without leaking `ApiKey`, timeout -> `504 AI_PROVIDER_TIMEOUT`, provider outage -> `502 AI_PROVIDER_UNAVAILABLE`, and deterministic caller cancellation propagation.
- Cross-user IDOR denial (`User B` receives `404 SOURCE_NOT_FOUND` on both `POST /analyze` and `GET /analysis`, and AI provider is never invoked).

---

## 7. Files Created & Modified

### Created (32 files)
- **Domain:**
  - `backend/src/EduScroll.Domain/Entities/TargetFeedLanguage.cs`
  - `backend/src/EduScroll.Domain/Entities/SourceAnalysis.cs`
  - `backend/src/EduScroll.Domain/Entities/SourceTopic.cs`
  - `backend/src/EduScroll.Domain/Entities/SourceSearchQuery.cs`
  - `backend/src/EduScroll.Domain/Exceptions/InvalidSourceAnalysisDomainException.cs`
- **Application:**
  - `backend/src/EduScroll.Application/AiAnalysis/Contracts/AnalyzeSourceRequest.cs`
  - `backend/src/EduScroll.Application/AiAnalysis/Contracts/SourceAnalysisDto.cs`
  - `backend/src/EduScroll.Application/AiAnalysis/Models/AiAnalysisModels.cs`
  - `backend/src/EduScroll.Application/AiAnalysis/Ports/IAiAnalysisProvider.cs`
  - `backend/src/EduScroll.Application/AiAnalysis/Ports/ISourceAnalysisRepository.cs`
  - `backend/src/EduScroll.Application/AiAnalysis/Ports/ISourceAnalysisService.cs`
  - `backend/src/EduScroll.Application/AiAnalysis/Services/AiAnalysisOutputValidator.cs`
  - `backend/src/EduScroll.Application/AiAnalysis/Services/SourceAnalysisService.cs`
- **Infrastructure:**
  - `backend/src/EduScroll.Infrastructure/Configuration/AiProviderOptions.cs`
  - `backend/src/EduScroll.Infrastructure/Configuration/AiProviderOptionsValidator.cs`
  - `backend/src/EduScroll.Infrastructure/AiAnalysis/IAiHttpTransport.cs`
  - `backend/src/EduScroll.Infrastructure/AiAnalysis/AiAnalysisPromptBuilder.cs`
  - `backend/src/EduScroll.Infrastructure/AiAnalysis/OllamaAiAnalysisProvider.cs`
  - `backend/src/EduScroll.Infrastructure/Persistence/Configurations/SourceAnalysisConfiguration.cs`
  - `backend/src/EduScroll.Infrastructure/Persistence/Configurations/SourceTopicConfiguration.cs`
  - `backend/src/EduScroll.Infrastructure/Persistence/Configurations/SourceSearchQueryConfiguration.cs`
  - `backend/src/EduScroll.Infrastructure/Persistence/SourceAnalysisRepository.cs`
  - `backend/src/EduScroll.Infrastructure/Persistence/Migrations/20261126182928_AddSourceAnalysisTables.cs`
  - `backend/src/EduScroll.Infrastructure/Persistence/Migrations/20261126182928_AddSourceAnalysisTables.Designer.cs`
  - `backend/src/EduScroll.Infrastructure/Persistence/Migrations/20261126184956_AddMultiLanguageFeedQueries.cs`
  - `backend/src/EduScroll.Infrastructure/Persistence/Migrations/20261126184956_AddMultiLanguageFeedQueries.Designer.cs`
- **API:**
  - `backend/src/EduScroll.API/Controllers/SourceAnalysisController.cs`
- **Tests:**
  - `backend/tests/EduScroll.Domain.Tests/SourceAnalysisTests.cs`
  - `backend/tests/EduScroll.Application.Tests/AiAnalysis/AiAnalysisOutputValidatorTests.cs`
  - `backend/tests/EduScroll.Application.Tests/AiAnalysis/SourceAnalysisServiceTests.cs`
  - `backend/tests/EduScroll.API.IntegrationTests/AiAnalysis/OllamaAiAnalysisProviderTests.cs`
  - `backend/tests/EduScroll.API.IntegrationTests/AiAnalysis/SourceAnalysisEndpointsIntegrationTests.cs`
- **Report:**
  - `docs/SPRINT-5-REPORT.md`

### Modified (10 files)
- `backend/src/EduScroll.Application/DependencyInjection.cs`
- `backend/src/EduScroll.Infrastructure/DependencyInjection.cs`
- `backend/src/EduScroll.Infrastructure/Persistence/ApplicationDbContext.cs`
- `backend/src/EduScroll.Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`
- `backend/src/EduScroll.API/appsettings.json`
- `backend/src/EduScroll.API/appsettings.Development.json`
- `backend/src/EduScroll.API/appsettings.Testing.json`
- `backend/src/EduScroll.API/appsettings.Example.json`
- `backend/tests/EduScroll.API.IntegrationTests/Persistence/DatabaseMigrationAndReadinessTests.cs`
- `README.md`

---

## 8. Scope Boundary Confirmation

Confirmed: **Sprint 6 (YouTube Discovery), Sprint 7 (Video Ranking), Sprint 8 (Full Pipeline Orchestration), and Sprint 9 (Flutter Feed UI) were NOT started.** No YouTube API clients, video metadata tables, ranking logic, background job queues, or Flutter feed changes were introduced.
