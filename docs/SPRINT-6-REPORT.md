# EduScroll — Sprint 6 Completion Report
## YouTube Discovery (Database-First Architecture)

- **Project:** EduScroll (Educational Short-Video Discovery Platform)
- **Sprint:** 6 — YouTube Discovery (Steps 1–4)
- **Date:** 2026-11-27
- **Backend Stack:** ASP.NET Core 10 Web API (`backend/`), Clean Architecture, EF Core 10, Microsoft SQL Server, ASP.NET Core Identity + JWT Bearer, YouTube Atom RSS + YouTube Data API v3 (`videos.list` & `search.list`)

---

## 1. Executive Summary

Sprint 6 implemented EduScroll's **YouTube Discovery** subsystem using a **Database-First** architecture backed by **Microsoft SQL Server**:

1. **Step 1 (Video Catalog Schema & Repositories):** Defined `WhitelistedChannel`, `CatalogVideo`, `DiscoverySource`, `InvalidVideoCatalogDomainException`, EF Core configurations, migration `20261126193452_AddVideoCatalogTables`, and SQL Server repositories (`WhitelistedChannelRepository`, `VideoCatalogRepository`).
2. **Step 2 (Whitelist RSS Ingestion + Batched `videos.list` Enrichment):** Implemented `YouTubeOptions` + startup `YouTubeOptionsValidator`, XXE-safe Atom RSS ingestion (`YouTubeRssFeedClient`), batched `videos.list` metadata enrichment (`YouTubeVideoMetadataClient`, max 50 IDs/request, ISO-8601 duration parsing via `XmlConvert.ToTimeSpan`, strict `ApiKey` redaction), and `WhitelistCatalogSyncService` (`DiscoverySource.WhitelistRss`).
3. **Step 3 (Database-First Discovery Engine + `search.list` Fallback + Write-Through Cache):** Implemented `VideoDiscoveryEngine` and `YouTubeSearchFallbackClient` (`type=video`, `videoEmbeddable=true`, `safeSearch=strict`, `relevanceLanguage`). Local SQL Server catalog queries run first; `search.list` triggers only when local matches fall below `MinLocalCandidatesThreshold`, enriches via `videos.list`, persists write-through results as `DiscoverySource.YouTubeApiFallback`, and degrades gracefully on quota/network errors.
4. **Step 4 (Discovery API Endpoints, SQL Server Integration Tests & Report):** Exposed authenticated endpoints in `VideoDiscoveryController` (`POST /api/v1/sources/{sourceId}/discover`, `GET /api/v1/discovery/channels`, `POST /api/v1/discovery/channels`, `POST /api/v1/discovery/whitelist/sync`) with RFC 9457 `ProblemDetails` mapping and verified end-to-end SQL Server integration tests.

All architectural and security boundaries were preserved:
- **Metadata-Only Rule (`BR-001`):** Only video identifiers (`YouTubeVideoId`) and metadata fields are retrieved and stored; media streams are never downloaded.
- **Credential Isolation (`SEC-008`, `BR-008`):** `YouTube:ApiKey` stays exclusively in backend configuration/User Secrets, is optional in dev/test, rejects `REPLACE_WITH_` placeholders at startup, and is redacted across logs and exceptions.
- **Strict User Ownership (`SEC-003`):** Source discovery authenticates via JWT Bearer (`User.GetRequiredUserId()`) and enforces ownership before inspecting analysis or invoking discovery.
- **Scope Discipline:** **Sprint 7 (Video Ranking & Scoring), Sprint 8 (Background Job Schedulers / Full Pipeline Orchestration), and Sprint 9 (Flutter Feed UI) were NOT started.**

---

## 2. Pre-Coding Audit Alignment

- **`docs/05-video-discovery/discovery-design.md`:** Aligned with provider-agnostic ports (`IYouTubeRssFeedClient`, `IYouTubeVideoMetadataClient`, `IYouTubeSearchFallbackClient`, `IVideoDiscoveryEngine`, `IVideoDiscoveryService`), in-memory `YouTubeVideoId` deduplication before `videos.list`, null-safe handling of missing statistics/thumbnails/descriptions, `videoEmbeddable=true`, and graceful `403 quotaExceeded` degradation.
- **`backend/src/EduScroll.API/Controllers/SourceAnalysisController.cs` & `GlobalExceptionHandler.cs`:** Matched `[Authorize]`, `User.GetRequiredUserId()`, `[RequestSizeLimit]`, OpenAPI `[EndpointSummary]`/`[ProducesResponseType]` attributes, and RFC 9457 `ProblemDetails` (`application/problem+json`).
- **`CleanArchitectureDependencyTests`:** Verified `EduScroll.Domain` has zero framework/HTTP/JSON dependencies, `EduScroll.Application` references only `EduScroll.Domain`, and `VideoDiscoveryController` has zero dependencies on `EduScroll.Infrastructure`.

---

## 3. API Contract

All endpoints live in `backend/src/EduScroll.API/Controllers/VideoDiscoveryController.cs`, require `[Authorize]` (JWT Bearer), and return RFC 9457 `ProblemDetails` (`application/problem+json`) on errors.

### `POST /api/v1/sources/{sourceId:guid}/discover`
Runs Database-First video discovery for an analyzed, user-owned source using its persisted `SourceAnalysis` `SearchQueries` and `TargetFeedLanguage`.

- **Optional Request Body (`DiscoverSourceVideosRequest`, max `4 KB`):**
  ```json
  {
    "maxCandidates": 20
  }
  ```
- **Success (`200 OK`, `VideoDiscoveryResultDto`):**
  - Returns deduplicated `candidates` (`VideoCandidateDto[]`) plus `localCandidateCount`, `fallbackCandidateCount`, `fallbackTriggered`, and `fallbackSkippedReason`.
- **Error Mapping:**
  - `400 Bad Request` — `VIDEO_DISCOVERY_INVALID_CANDIDATE_COUNT`, `VIDEO_DISCOVERY_INVALID_LANGUAGE`, `VIDEO_DISCOVERY_INVALID_TARGET_LANGUAGE`, `VIDEO_DISCOVERY_QUERIES_REQUIRED`
  - `401 Unauthorized` — Missing/invalid JWT (`UNAUTHORIZED`)
  - `404 Not Found` — `SOURCE_NOT_FOUND` (missing source or owned by another user)
  - `409 Conflict` — `SOURCE_NOT_ANALYZED` (source has not been analyzed yet)

### `GET /api/v1/discovery/channels`
Lists curated whitelisted educational YouTube channels with optional query filters `language` (`"en"`/`"ar"`) and `isActive` (`true`/`false`).

- **Success (`200 OK`):** `IReadOnlyList<WhitelistedChannelDto>`
- **Errors:** `400 Bad Request` (`CHANNEL_LANGUAGE_INVALID`), `401 Unauthorized`

### `POST /api/v1/discovery/channels`
Validates and adds a curated educational YouTube channel (`max 8 KB`).

- **Request Body (`CreateWhitelistedChannelRequest`):**
  ```json
  {
    "youTubeChannelId": "UCYO_jab_esuFRV4b17AJtAw",
    "name": "3Blue1Brown",
    "language": "en",
    "topicCategory": "Mathematics",
    "trustScore": 0.95,
    "isActive": true
  }
  ```
- **Success (`201 Created`):** `WhitelistedChannelDto`
- **Errors:**
  - `400 Bad Request` — `CHANNEL_ID_REQUIRED`, `CHANNEL_ID_TOO_LONG`, `CHANNEL_NAME_REQUIRED`, `CHANNEL_NAME_TOO_LONG`, `CHANNEL_LANGUAGE_REQUIRED`, `CHANNEL_LANGUAGE_INVALID`, `CHANNEL_CATEGORY_REQUIRED`, `CHANNEL_CATEGORY_TOO_LONG`, `CHANNEL_TRUST_SCORE_INVALID`
  - `401 Unauthorized`
  - `409 Conflict` — `CHANNEL_ALREADY_EXISTS` (duplicate `YouTubeChannelId`)

### `POST /api/v1/discovery/whitelist/sync`
Triggers `IWhitelistCatalogSyncService` (`max 4 KB`), optionally scoped by `language` (body `SyncWhitelistRequest` or query `?language=en|ar`), ingesting Atom RSS feeds and enriching metadata via `videos.list`.

- **Success (`200 OK`):** `WhitelistSyncSummaryDto` (`activeChannelsCount`, `succeededChannelsCount`, `failedChannelsCount`, `fetchedRssEntriesCount`, `deduplicatedVideosCount`, `enrichedVideosCount`, `upsertedVideosCount`, `completedAtUtc`)
- **Errors:** `400 Bad Request` (`VIDEO_LANGUAGE_INVALID`), `401 Unauthorized`

---

## 4. Configuration Keys (`YouTube`)

Bound via `YouTubeOptions` and validated at startup by `YouTubeOptionsValidator` (placeholder `ApiKey` values containing `REPLACE_WITH_` are rejected at startup). No real secrets are committed; `appsettings.Example.json` contains placeholder guidance only.

| Key | Default | Validation / Purpose |
|---|---|---|
| `YouTube:ApiBaseUrl` | `https://www.googleapis.com/youtube/v3` | Valid absolute HTTP/HTTPS endpoint |
| `YouTube:RssBaseUrl` | `https://www.youtube.com/feeds/videos.xml` | Valid absolute HTTP/HTTPS RSS endpoint |
| `YouTube:ApiKey` | `""` | Optional in dev/test; set via User Secrets / `YouTube__ApiKey` (never logged) |
| `YouTube:TimeoutSeconds` | `15` | Per-attempt timeout (`1..120` sec) |
| `YouTube:MaxRetries` | `2` | Bounded transient retries (`0..5`) |
| `YouTube:RetryBaseDelayMilliseconds` | `500` | Exponential backoff base delay (`0..10000` ms) |
| `YouTube:MetadataBatchSize` | `50` | Max IDs per `videos.list` batch (`1..50`) |
| `YouTube:MaxVideosPerChannelPerSync` | `15` | Max RSS entries ingested per channel per sync (`1..50`) |
| `YouTube:EnableSearchFallback` | `true` | Enables/disables `search.list` fallback when local catalog is below threshold |
| `YouTube:MinLocalCandidatesThreshold` | `15` | Minimum local catalog matches before skipping `search.list` (`>= 1`) |
| `YouTube:MaxFallbackQueriesPerDiscovery` | `2` | Max `search.list` queries executed per discovery run (`1..5`) |
| `YouTube:MaxFallbackResultsPerQuery` | `10` | Max `search.list` items requested per query (`1..25`) |

---

## 5. SQL Server Persistence, RSS Ingestion & Discovery Architecture

- **Migration:** `backend/src/EduScroll.Infrastructure/Persistence/Migrations/20261126193452_AddVideoCatalogTables.cs`
- **SQL Server Tables & Indexes:**
  - `WhitelistedChannels` (`Id` PK, `YouTubeChannelId` varchar(64) unique index `IX_WhitelistedChannels_YouTubeChannelId`, `Name`, `Language` varchar(8), `TopicCategory`, `TrustScore`, `IsActive`, `LastSyncedAtUtc`, `CreatedAtUtc`; composite index `IX_WhitelistedChannels_IsActive_Language`).
  - `CatalogVideos` (`Id` PK, `YouTubeVideoId` varchar(32) unique index `IX_CatalogVideos_YouTubeVideoId`, `WhitelistedChannelId` nullable FK -> `WhitelistedChannels.Id` `ON DELETE SET NULL`, `ChannelExternalId`, `ChannelName`, `Title`, `Description`, `ThumbnailUrl`, `Language` varchar(8), `DurationSeconds`, `ViewCount`, `LikeCount`, `PublishedAtUtc`, `DiscoverySource` string enum, `CreatedAtUtc`, `UpdatedAtUtc`; composite index `IX_CatalogVideos_Language_PublishedAtUtc` (`Language ASC, PublishedAtUtc DESC`); index `IX_CatalogVideos_WhitelistedChannelId`).
- **Whitelist RSS + `videos.list` Pipeline:** `YouTubeRssFeedClient` parses YouTube Atom XML using `XmlReaderSettings` (`DtdProcessing.Prohibit`, `XmlResolver = null`), isolates per-channel failures, deduplicates video IDs, chunks into batches of max 50, enriches via `YouTubeVideoMetadataClient`, safely falls back to RSS fields if metadata is unavailable, upserts via `VideoCatalogRepository.UpsertRangeAsync`, and marks `LastSyncedAtUtc` only for succeeded channels.
- **Database-First Discovery + Write-Through Cache:** `VideoDiscoveryEngine` queries `VideoCatalogRepository.SearchByLanguageAsync` per language (leveraging `IX_CatalogVideos_Language_PublishedAtUtc`), interleaves `"en"` and `"ar"` for `TargetFeedLanguage.Both`, short-circuits with `0` `search.list` quota when `LocalCandidateCount >= MinLocalCandidatesThreshold`, and otherwise calls `YouTubeSearchFallbackClient`, batch-enriches via `YouTubeVideoMetadataClient`, persists `DiscoverySource.YouTubeApiFallback` into `CatalogVideos`, and degrades gracefully on quota/429/timeout/auth failures.

---

## 6. Verification & Test Results

All automated unit and SQL Server integration tests use mocked HTTP handlers/transports (zero live YouTube or Ollama network calls).

### Commands Executed
```powershell
dotnet build backend/EduScroll.sln
dotnet test backend/EduScroll.sln --no-build --filter "FullyQualifiedName~VideoDiscoveryEndpointsIntegrationTests"
dotnet test backend/EduScroll.sln --no-build
```

### Exact Results
- **Build (`dotnet build backend/EduScroll.sln`):** **PASS** — `0 Warning(s), 0 Error(s)` (`4.38 s`, exit `0`).
- **Filtered Step 4 Integration Tests (`VideoDiscoveryEndpointsIntegrationTests`):** **5 / 5 Passed** (`0` Failed, `0` Skipped, `2 s`).
- **Full Solution Test Suite (`dotnet test backend/EduScroll.sln --no-build`):** **386 / 386 Passed** (`0` Failed, `0` Skipped, exit `0`):
  - `EduScroll.Domain.Tests`: **71 / 71 Passed** (`138 ms`)
  - `EduScroll.Application.Tests`: **88 / 88 Passed** (`137 ms`)
  - `EduScroll.Architecture.Tests`: **5 / 5 Passed** (`747 ms`)
  - `EduScroll.API.IntegrationTests`: **222 / 222 Passed** (`6 s`)

### Runtime Smoke-Check (`EduScroll.API` in `Development` on `http://localhost:5221`)
- Startup `YouTubeOptionsValidator` succeeded (`Application started`).
- `GET /health/live` -> `200 OK` (`{"status":"Healthy"}`).
- `GET /health/ready` -> `200 OK` (`{"status":"Healthy"}` against SQL Server `EduScroll_Dev`).
- `GET /swagger/index.html` -> `200 OK` (`735 bytes`) and `GET /openapi/v1.json` -> `200 OK` (`57242 bytes`, exposing `/api/v1/sources/{sourceId}/discover`, `/api/v1/discovery/channels`, `/api/v1/discovery/whitelist/sync`).
- Unauthenticated `GET /api/v1/discovery/channels` -> `401 Unauthorized`.

---

## 7. Files Created & Modified Across Sprint 6 (Steps 1–4)

### Created
- **Domain & Persistence (Step 1):**
  - `backend/src/EduScroll.Domain/Entities/CatalogVideo.cs`
  - `backend/src/EduScroll.Domain/Entities/DiscoverySource.cs`
  - `backend/src/EduScroll.Domain/Entities/WhitelistedChannel.cs`
  - `backend/src/EduScroll.Domain/Exceptions/InvalidVideoCatalogDomainException.cs`
  - `backend/src/EduScroll.Infrastructure/Persistence/Configurations/CatalogVideoConfiguration.cs`
  - `backend/src/EduScroll.Infrastructure/Persistence/Configurations/WhitelistedChannelConfiguration.cs`
  - `backend/src/EduScroll.Infrastructure/Persistence/VideoCatalogRepository.cs`
  - `backend/src/EduScroll.Infrastructure/Persistence/WhitelistedChannelRepository.cs`
  - `backend/src/EduScroll.Infrastructure/Persistence/Migrations/20261126193452_AddVideoCatalogTables.cs`
  - `backend/src/EduScroll.Infrastructure/Persistence/Migrations/20261126193452_AddVideoCatalogTables.Designer.cs`
- **Application (Steps 1–4):**
  - `backend/src/EduScroll.Application/VideoDiscovery/Contracts/CreateWhitelistedChannelRequest.cs`
  - `backend/src/EduScroll.Application/VideoDiscovery/Contracts/DiscoverSourceVideosRequest.cs`
  - `backend/src/EduScroll.Application/VideoDiscovery/Contracts/SyncWhitelistRequest.cs`
  - `backend/src/EduScroll.Application/VideoDiscovery/Contracts/VideoCandidateDto.cs`
  - `backend/src/EduScroll.Application/VideoDiscovery/Contracts/VideoDiscoveryRequest.cs`
  - `backend/src/EduScroll.Application/VideoDiscovery/Contracts/VideoDiscoveryResultDto.cs`
  - `backend/src/EduScroll.Application/VideoDiscovery/Contracts/WhitelistedChannelDto.cs`
  - `backend/src/EduScroll.Application/VideoDiscovery/Contracts/WhitelistSyncSummaryDto.cs`
  - `backend/src/EduScroll.Application/VideoDiscovery/Models/YouTubeDiscoveryModels.cs`
  - `backend/src/EduScroll.Application/VideoDiscovery/Ports/IVideoCatalogRepository.cs`
  - `backend/src/EduScroll.Application/VideoDiscovery/Ports/IVideoDiscoveryEngine.cs`
  - `backend/src/EduScroll.Application/VideoDiscovery/Ports/IVideoDiscoveryService.cs`
  - `backend/src/EduScroll.Application/VideoDiscovery/Ports/IWhitelistedChannelRepository.cs`
  - `backend/src/EduScroll.Application/VideoDiscovery/Ports/IWhitelistCatalogSyncService.cs`
  - `backend/src/EduScroll.Application/VideoDiscovery/Ports/IYouTubeRssFeedClient.cs`
  - `backend/src/EduScroll.Application/VideoDiscovery/Ports/IYouTubeSearchFallbackClient.cs`
  - `backend/src/EduScroll.Application/VideoDiscovery/Ports/IYouTubeVideoMetadataClient.cs`
  - `backend/src/EduScroll.Application/VideoDiscovery/Services/CatalogVideoMappingHelper.cs`
  - `backend/src/EduScroll.Application/VideoDiscovery/Services/VideoDiscoveryEngine.cs`
  - `backend/src/EduScroll.Application/VideoDiscovery/Services/VideoDiscoveryService.cs`
  - `backend/src/EduScroll.Application/VideoDiscovery/Services/WhitelistCatalogSyncService.cs`
- **Infrastructure (Steps 2–3):**
  - `backend/src/EduScroll.Infrastructure/Configuration/YouTubeOptions.cs`
  - `backend/src/EduScroll.Infrastructure/Configuration/YouTubeOptionsValidator.cs`
  - `backend/src/EduScroll.Infrastructure/VideoDiscovery/YouTubeDataApiHelper.cs`
  - `backend/src/EduScroll.Infrastructure/VideoDiscovery/YouTubeHttpRetryHelper.cs`
  - `backend/src/EduScroll.Infrastructure/VideoDiscovery/YouTubeRssFeedClient.cs`
  - `backend/src/EduScroll.Infrastructure/VideoDiscovery/YouTubeSearchFallbackClient.cs`
  - `backend/src/EduScroll.Infrastructure/VideoDiscovery/YouTubeVideoMetadataClient.cs`
- **API (Step 4):**
  - `backend/src/EduScroll.API/Controllers/VideoDiscoveryController.cs`
- **Tests (Steps 1–4):**
  - `backend/tests/EduScroll.Domain.Tests/VideoCatalogTests.cs`
  - `backend/tests/EduScroll.Application.Tests/VideoDiscovery/VideoDiscoveryEngineTests.cs`
  - `backend/tests/EduScroll.Application.Tests/VideoDiscovery/WhitelistCatalogSyncServiceTests.cs`
  - `backend/tests/EduScroll.API.IntegrationTests/VideoDiscovery/VideoDiscoveryEndpointsIntegrationTests.cs`
  - `backend/tests/EduScroll.API.IntegrationTests/VideoDiscovery/YouTubeOptionsValidatorTests.cs`
  - `backend/tests/EduScroll.API.IntegrationTests/VideoDiscovery/YouTubeRssFeedClientTests.cs`
  - `backend/tests/EduScroll.API.IntegrationTests/VideoDiscovery/YouTubeSearchFallbackClientTests.cs`
  - `backend/tests/EduScroll.API.IntegrationTests/VideoDiscovery/YouTubeVideoMetadataClientTests.cs`
- **Report:**
  - `docs/SPRINT-6-REPORT.md`

### Modified
- `backend/src/EduScroll.Application/DependencyInjection.cs`
- `backend/src/EduScroll.Infrastructure/DependencyInjection.cs`
- `backend/src/EduScroll.Infrastructure/Persistence/ApplicationDbContext.cs`
- `backend/src/EduScroll.Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`
- `backend/src/EduScroll.API/appsettings.json`
- `backend/src/EduScroll.API/appsettings.Development.json`
- `backend/src/EduScroll.API/appsettings.Testing.json`
- `backend/src/EduScroll.API/appsettings.Example.json`
- `backend/tests/EduScroll.API.IntegrationTests/Persistence/DatabaseMigrationAndReadinessTests.cs`

---

## 8. Scope Boundary Confirmation & Deferred Items

Confirmed: **Sprint 7 (Video Ranking & Scoring), Sprint 8 (Background Schedulers / Full Pipeline Orchestration), and Sprint 9 (Flutter Feed UI) were NOT started.**
Intentionally deferred:
- **Sprint 7:** Multi-factor relevance/quality/duration/trust scoring, duplicate/diversity filtering, and feed blending formulas.
- **Sprint 8:** Automated recurring background sync jobs/schedulers for `IWhitelistCatalogSyncService`, asynchronous pipeline orchestration, and feed pagination/caching endpoints.
