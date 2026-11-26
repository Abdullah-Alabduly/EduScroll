# EduScroll — Sprint 4 Completion Report
## Source Ingestion & Content Extraction

- **Project:** EduScroll (Educational Short-Video Discovery Platform)
- **Sprint:** 4 — Source Ingestion & Content Extraction
- **Branch:** `feature/sprint-4-source-ingestion` (branched from `feature/sprint-3-flutter-foundation`, preserving all Sprint 1–3 branches and commits untouched)
- **Date:** 2026-11-25
- **Backend Stack:** ASP.NET Core 10 Web API (`backend/`), Clean Architecture, EF Core 10, Microsoft SQL Server, ASP.NET Core Identity + JWT Bearer, `AngleSharp` (`1.11.0`)
- **Mobile Stack:** Flutter `3.41.4`, Dart `3.11.1`, Riverpod (`flutter_riverpod 2.6.1`), Dio (`5.11.1`), GoRouter (`17.5.0`), `flutter_secure_storage` (`11.2.0`), `file_picker` (`13.4.1`), `syncfusion_flutter_pdf` (`35.1.43`)

---

## 1. Executive Summary

Sprint 4 delivered EduScroll's end-to-end **Source Ingestion & Content Extraction** subsystem across the ASP.NET Core 10 backend, Microsoft SQL Server database, and Flutter mobile/desktop/web application, strictly within scope (zero AI, LLM, YouTube, ranking, or video feed logic).

Authenticated learners can now:
- Select a PDF on-device (`<= 15 MB`), validate its `.pdf` extension and `%PDF-` signature, extract normalized UTF-8 text and title metadata **off the UI thread** inside a background isolate (`compute()`), and submit **text + metadata only** (never the raw PDF binary) to `POST /api/v1/sources`.
- Submit public HTTP/HTTPS web article URLs to `POST /api/v1/sources/from-url`, where the backend enforces SSRF, DNS-rebinding, redirect, timeout, `Content-Type`, and streamed byte-limit protections before extracting readable article text via `AngleSharp`.
- Browse their personal paginated library (`GET /api/v1/sources`), inspect full plain-text source details (`GET /api/v1/sources/{id}`), and permanently delete their own sources (`DELETE /api/v1/sources/{id}`), with strict server-side `OwnerUserId == JwtSub` isolation verified against cross-user access.

---

## 2. Repository Inspection

Inspection prior to coding established and reused the existing verified foundation:
- **Git & Branching:** Started cleanly on `feature/sprint-3-flutter-foundation` (`e49d869`); created `feature/sprint-4-source-ingestion` without modifying prior branches.
- **Backend Architecture Reused:** Clean Architecture projects (`EduScroll.Domain`, `EduScroll.Application`, `EduScroll.Infrastructure`, `EduScroll.API`), `Directory.Build.props` (`net10.0`, `Nullable`, `TreatWarningsAsErrors`), `ApplicationDbContext`, SQL Server `Migrations`, RFC 9457 `ProblemDetails` via `GlobalExceptionHandler`, `CorrelationIdMiddleware`, startup-validated `IValidateOptions`, JWT `sub` claim authentication, and `CleanArchitectureDependencyTests`.
- **Flutter Architecture Reused:** Feature-first layered `mobile/lib/` layout, single shared `Dio` client (`dioProvider`) with `CorrelationIdInterceptor` and `AuthInterceptor` + single-flight `SessionRefresher`, `SecureTokenStorage`, `GoRouter` auth guards (`resolveAuthRedirect`), `ErrorMapper`, and shared UI widgets (`AppTextField`, `PrimaryButton`, `SecondaryButton`, `EmptyState`, `ErrorView`, `LoadingIndicator`).
- **Scope & Constraints Honored:** SQL Server retained (no PostgreSQL); local Flutter PDF extraction enforced per `ADR-002` (no backend PDF upload/extraction); no speculative AI or YouTube fields introduced.

---

## 3. Architecture

### Backend Source Subsystem (Clean Architecture)
- **Domain (`EduScroll.Domain`):** Pure `Source` aggregate (`SourceType`, `SourceStatus`, `InvalidSourceDomainException`, `CreatePdf`, `CreateWebArticle`, `IsOwnedBy`) with zero framework references.
- **Application (`EduScroll.Application`):** Ports (`ISourceService`, `ISourceRepository`, `IWebContentFetcher`), DTOs (`CreatePdfSourceRequest`, `CreateUrlSourceRequest`, `SourceSummaryDto`, `SourceDetailDto`, `PagedSourcesResponse`), `SourceTextNormalizer`, `SourceRequestValidator`, `SourceService`, and transport-agnostic exception types (`UnprocessableEntityAppException`, `PayloadTooLargeAppException`, `GatewayTimeoutAppException`, `ExternalDependencyAppException`).
- **Infrastructure (`EduScroll.Infrastructure`):** `SourceConfiguration`, `SourceRepository` (SQL-level `Substring` preview projection for list views + atomic `ExecuteDeleteAsync` owner-scoped delete), `SourceIngestionOptions` + startup validator, `SsrfUrlGuard`, `IDnsResolver` / `SystemDnsResolver`, `IWebHttpTransport` / `SecureSocketsHttpTransport`, `HtmlArticleExtractor` (`AngleSharp`), and `SecureWebContentFetcher`.
- **API (`EduScroll.API`):** `SourcesController` (`/api/v1/sources`), shared `ClaimsPrincipalExtensions.GetRequiredUserId()` (reused by `AuthController`), request body size limits, and extended `GlobalExceptionHandler` mappings (`413`, `422`, `504`).

### Flutter Source Module (`mobile/lib/features/sources/`)
- **Domain:** `SourceType`, `SourceStatus`, `SourceSummary`, `SourceDetail`, `PagedSources`, `PickedPdfFile`, `ExtractedPdfContent`, `SourceRepository`, `PdfFilePicker`, `PdfTextExtractorService`.
- **Data:** JSON models, `SourceApiService` (reusing `dioProvider` + `CancelToken`), `SourceRepositoryImpl`, `SourceValidation`, `PlatformPdfFilePicker` (`file_picker 13.4.1`), and `LocalPdfTextExtractor` (`syncfusion_flutter_pdf 35.1.43` inside `compute()`).
- **Presentation:** Riverpod notifiers (`PdfIngestionController`, `UrlIngestionController`, `SourcesListController`) + `sourceDetailProvider`, `MainShellScreen` bottom navigation (`Home` / `Add Source` / `My Sources`), and 5 screens (`AddSourceScreen`, `PdfImportScreen`, `UrlImportScreen`, `MySourcesScreen`, `SourceDetailScreen`).

---

## 4. Database

- **Table:** `Sources` (EF Core entity `Source` mapped via `SourceConfiguration`).
- **Columns:** `Id` (`uniqueidentifier` PK, client-generated GUID), `OwnerUserId` (`uniqueidentifier` FK -> `AspNetUsers.Id`, `ON DELETE CASCADE`), `Title` (`nvarchar(250)`), `SourceType` (`nvarchar(32)`), `OriginalUrl` (`nvarchar(2048)` nullable), `OriginalFileName` (`nvarchar(255)` nullable), `ExtractedText` (`nvarchar(max)`, **not indexed**), `ContentLength` (`int`), `Status` (`nvarchar(32)`), `CreatedAtUtc` (`datetimeoffset`), `UpdatedAtUtc` (`datetimeoffset`).
- **Index:** Composite index `IX_Sources_OwnerUserId_CreatedAtUtc` (`OwnerUserId ASC, CreatedAtUtc DESC`) supporting fast owner-scoped pagination.
- **Migration Created & Verified:** `20261125185524_AddSourcesTable` generated cleanly without altering Identity or `RefreshTokens` schemas; applied cleanly to `EduScroll_Dev` on `(localdb)\MSSQLLocalDB` (verified existing user row preserved and `Sources` table created) and verified on ephemeral SQL Server test databases in `DatabaseMigrationAndReadinessTests` and live E2E tests.

---

## 5. Backend

### Implemented Endpoints (`/api/v1/sources`, all `[Authorize]`)
- `POST /api/v1/sources` — Creates a `Pdf` source from locally extracted UTF-8 text (`[RequestSizeLimit(1_048_576)]`), returns `201 Created` (`SourceDetailDto`).
- `POST /api/v1/sources/from-url` — Securely fetches and extracts a public HTML article (`[RequestSizeLimit(16_384)]`), returns `201 Created` (`SourceDetailDto`).
- `GET /api/v1/sources?page=1&pageSize=20` — Returns `200 OK` (`PagedSourcesResponse` of `SourceSummaryDto` with short `textPreview` projected in SQL without transferring full `ExtractedText`; max `pageSize = 50`).
- `GET /api/v1/sources/{id}` — Returns `200 OK` (`SourceDetailDto` including full `ExtractedText`) only for owner, else `404 SOURCE_NOT_FOUND`.
- `DELETE /api/v1/sources/{id}` — Deletes only owned source (`204 No Content`), else `404 SOURCE_NOT_FOUND`.

### HTML Article Extraction & Configuration
- `HtmlArticleExtractor` (`AngleSharp 1.11.0`) resolves best title (`og:title` / `twitter:title` / `h1` / `<title>`), strips scripts, styles, navigation, headers, footers, asides, forms, iframes, SVGs, cookie banners, ads, and sidebars, prefers `<article>` / `<main>`, and preserves clean paragraph boundaries.
- Configured via validated `SourceIngestion` options (`MinExtractedTextLength=50`, `MaxExtractedTextLength=200000`, `PreviewMaxLength=240`, `DefaultPageSize=20`, `MaxPageSize=50`, `ConnectTimeoutSeconds=10`, `RequestTimeoutSeconds=20`, `MaxResponseBytes=2097152`, `MaxRedirects=3`, `AllowedContentTypes=["text/html","application/xhtml+xml"]`).

---

## 6. Flutter

- **Dependencies Added:** `file_picker: ^13.4.1` and `syncfusion_flutter_pdf: ^35.1.43` (compatible with Flutter `3.41.4` / Dart `3.11.1`).
- **Social-First Dark Theme & Shell:** Refined `AppColors` / `AppTheme` dark surfaces to deep charcoal (`#0E0F14` / `#161822`) with violet accent (`#7C3AED` / `#A78BFA`), `ThemeMode.dark` default, and `MainShellScreen` bottom navigation (`Home` | `Add Source` | `My Sources`) while keeping `ProfileScreen` and all Sprint 3 routes/keys intact.
- **PDF Flow (`PdfImportScreen`):** All 8 states (`initial`, `selectingFile`, `fileSelected`, `extracting`, `preparing`, `submitting`, `success`, `failure`) with filename/size preview, optional custom title, honest step feedback (no fake percentages), active cancellation, and retry.
- **URL Flow (`UrlImportScreen`):** URL input, clipboard Paste action, client-side validation, duplicate-tap prevention, active `CancelToken` cancellation, backend processing feedback, input preservation on failure, and success actions.
- **Library & Details (`MySourcesScreen`, `SourceDetailScreen`):** Paginated list with pull-to-refresh, "Load more", empty/loading/error states, delete confirmation dialogs, and plain `SelectableText` rendering (never rendering extracted text as HTML). Automatic list cache clearing on logout or account switch.

---

## 7. Security

- **Authentication & Ownership Isolation:** Every Source endpoint requires JWT Bearer auth; `OwnerUserId` is derived strictly from JWT `sub`/`NameIdentifier` claims (any spoofed `ownerUserId` in JSON is ignored); all repository reads/deletes filter by `OwnerUserId == ownerUserId` and return `404 SOURCE_NOT_FOUND` for cross-user IDs (preventing both IDOR and enumeration).
- **SSRF & DNS Rebinding Protections (`SsrfUrlGuard`, `SecureSocketsHttpTransport`, `SecureWebContentFetcher`):**
  - Enforces `http://`/`https://` allowlist and rejects embedded credentials (`user:pass@`).
  - Blocks `localhost`, `*.localhost`, `*.local`, `*.internal`, `*.localdomain`, `*.home.arpa`, `metadata.google.internal`, unqualified internal hostnames, and all non-public IPv4/IPv6 ranges (loopback, RFC 1918, CGNAT `100.64.0.0/10`, link-local/cloud metadata `169.254.0.0/16`, multicast, broadcast/reserved, IPv6 ULA `fc00::/7`, link-local `fe80::/10`, IPv4-mapped `::ffff:0:0/96`, NAT64 `64:ff9b::/96`, 6to4 `2002::/16`, Teredo).
  - Validates resolved DNS addresses both pre-flight AND directly inside `SocketsHttpHandler.ConnectCallback` before opening the TCP socket to the verified IP.
  - Disables proxies, cookies, credentials, and auto-redirects; manually validates every redirect hop up to `MaxRedirects=3`.
  - Enforces HTML `Content-Type` allowlist, connect/request timeouts, and hard streamed byte cap (`2 MB`) blocking chunked/gzip bombs.
  - Zero internal IPs, hostnames, secrets, tokens, stack traces, or raw `ExtractedText` bodies logged or exposed in API ProblemDetails.
- **Client PDF Restrictions:** `.pdf` extension check, `15 MB` cap enforced before loading and during streaming read, `%PDF-` magic header check, raw PDF never leaves device, no silent text truncation.

---

## 8. Testing

| Suite | Command | Result |
|---|---|---|
| Backend Full Suite | `dotnet test backend/EduScroll.sln` | **203 / 203 Passed** (`0` Failed, `0` Skipped — Domain: `27`, Application: `54`, Architecture: `5`, API Integration incl. SSRF + SQL Server Sources CRUD + Auth regressions: `117`) |
| Flutter Static Analysis | `flutter analyze` (in `mobile/`) | **PASS** — `No issues found!` |
| Flutter Offline Unit & Widget Suite | `flutter test` (in `mobile/`) | **63 / 63 Passed** (`0` Failed, `2` live backend tests skipped when `EDUSCROLL_LIVE_API_URL` unset) |
| Live Flutter -> ASP.NET Core 10 -> SQL Server E2E | `EDUSCROLL_LIVE_API_URL=http://127.0.0.1:5299 flutter test test/integration/real_backend_auth_test.dart test/integration/real_backend_sources_test.dart -r expanded` | **2 / 2 Passed** (`0` Failed — full Auth + real Syncfusion off-UI-thread PDF extraction + Sources CRUD + live `169.254.169.254` SSRF block + cross-user IDOR denial against ephemeral LocalDB `EduScroll_Sprint4E2E`, cleanly dropped after run) |

---

## 9. Builds

| Target | Command | Result |
|---|---|---|
| Backend Solution | `dotnet build backend/EduScroll.sln` | **PASS** — `0 Warning(s), 0 Error(s)` |
| Flutter Android Debug APK | `flutter build apk --debug` | **PASS** — `√ Built build\app\outputs\flutter-apk\app-debug.apk` (exit `0`) |
| Flutter Windows Debug | `flutter build windows --debug` | **PASS** — `√ Built build\windows\x64\runner\Debug\eduscroll.exe` (exit `0`) |
| Flutter Web | `flutter build web --dart-define=APP_ENV=development --dart-define=API_BASE_URL=http://localhost:5221` | **PASS** — `√ Built build\web` (Wasm dry run succeeded, exit `0`) |

---

## 10. Acceptance Criteria

| ID | Criterion | Status | Evidence |
|---|---|---|---|
| S4-01 | Source architecture implemented following existing Clean Architecture | **PASS** | Domain/Application/Infrastructure/API separation verified by `EduScroll.Architecture.Tests` (5/5). |
| S4-02 | SQL Server Source persistence works | **PASS** | `SourceRepository` + `Sources` table verified in integration & live E2E tests. |
| S4-03 | Migration is created and verified | **PASS** | `20261125185524_AddSourcesTable` applied to `EduScroll_Dev` (data preserved) & verified in `DatabaseMigrationAndReadinessTests`. |
| S4-04 | Authenticated PDF Source creation works | **PASS** | Verified in `SourcesEndpointsIntegrationTests` & `real_backend_sources_test.dart`. |
| S4-05 | PDF extraction occurs locally in Flutter | **PASS** | `LocalPdfTextExtractor` verified with real generated multi-page/empty/encrypted/corrupt PDFs. |
| S4-06 | PDF extraction does not block the main UI | **PASS** | Parsing & extraction execute in a background isolate via `compute()`. |
| S4-07 | Extracted UTF-8 text is validated | **PASS** | Validated client-side (`SourceValidation`) and server-side (`SourceRequestValidator` + `Source`). |
| S4-08 | Original PDF is not uploaded | **PASS** | `POST /api/v1/sources` accepts JSON UTF-8 text + metadata only; verified in model & API tests. |
| S4-09 | Authenticated URL submission works | **PASS** | Verified in `SourcesEndpointsIntegrationTests`, `source_controllers_test.dart`, and `sources_flow_widget_test.dart`. |
| S4-10 | Backend URL fetching behind an abstraction | **PASS** | `IWebContentFetcher` port in `EduScroll.Application`; `SecureWebContentFetcher` in `EduScroll.Infrastructure`. |
| S4-11 | SSRF protections implemented and tested | **PASS** | `SsrfUrlGuard` + `SecureSocketsHttpTransport.ConnectCallback` + manual redirect checks verified in `WebExtractionSecurityTests` & `SourcesEndpointsIntegrationTests`. |
| S4-12 | HTML article extraction works | **PASS** | `HtmlArticleExtractor` (`AngleSharp`) strips scripts/ads/cookies/nav and preserves clean paragraphs (unit & integration verified). |
| S4-13 | Source ownership is enforced | **PASS** | `OwnerUserId` derived strictly from JWT `sub` and filtered on every repository query. |
| S4-14 | List own Sources works | **PASS** | Paginated `GET /api/v1/sources` with lightweight `TextPreview` projection verified in backend & Flutter tests. |
| S4-15 | Get Source details works | **PASS** | `GET /api/v1/sources/{id}` returns full `ExtractedText` for owner; rendered as plain `SelectableText` in Flutter. |
| S4-16 | Delete own Source works | **PASS** | `DELETE /api/v1/sources/{id}` verified end-to-end with confirmation dialog in Flutter. |
| S4-17 | Cross-user access is denied | **PASS** | User B list/get/delete of User A's source denied (`404 SOURCE_NOT_FOUND`) in integration & live E2E tests. |
| S4-18 | Flutter Source UI is implemented | **PASS** | Bottom nav shell (`Add Source`, `My Sources`), `AddSourceScreen`, `PdfImportScreen`, `UrlImportScreen`, `MySourcesScreen`, `SourceDetailScreen`. |
| S4-19 | Loading, empty, success, and failure states implemented | **PASS** | Verified in `sources_flow_widget_test.dart` and `source_controllers_test.dart`. |
| S4-20 | Flutter tests pass | **PASS** | 63 / 63 offline + 2 / 2 live E2E tests passed. |
| S4-21 | Backend tests pass | **PASS** | 203 / 203 tests passed. |
| S4-22 | Flutter analyze passes | **PASS** | `flutter analyze`: `No issues found!`. |
| S4-23 | Existing Sprint 1–3 functionality remains intact | **PASS** | All Sprint 1–3 backend auth/health/CORS tests and Flutter auth/app-flow tests pass unchanged. |
| S4-24 | Documentation is updated | **PASS** | Updated `README.md`, `mobile/README.md`, `docs/03-architecture/flutter-architecture.md`, and created `docs/SPRINT-4-REPORT.md`. |
| S4-25 | No AI, YouTube, or recommendation functionality introduced | **PASS** | Zero AI/LLM/YouTube/feed code or packages added. |

---

## 11. Files Created & Modified

### Created — Backend (`backend/`)
- **Domain:** `Source.cs`, `SourceStatus.cs`, `SourceType.cs`, `InvalidSourceDomainException.cs`
- **Application:** `ExternalDependencyAppException.cs`, `GatewayTimeoutAppException.cs`, `PayloadTooLargeAppException.cs`, `UnprocessableEntityAppException.cs`, `Sources/Contracts/*` (5 DTO files), `Sources/Models/SourceModels.cs`, `Sources/Ports/*` (3 interfaces), `Sources/Services/*` (`SourceRequestValidator.cs`, `SourceService.cs`, `SourceTextNormalizer.cs`)
- **Infrastructure:** `Configuration/SourceIngestionOptions.cs`, `Configuration/SourceIngestionOptionsValidator.cs`, `Persistence/Configurations/SourceConfiguration.cs`, `Persistence/SourceRepository.cs`, `Persistence/Migrations/20261125185524_AddSourcesTable.cs` (+ `.Designer.cs`), `WebExtraction/*` (`IDnsResolver.cs`, `SsrfUrlGuard.cs`, `IWebHttpTransport.cs`, `HtmlArticleExtractor.cs`, `SecureWebContentFetcher.cs`)
- **API:** `Controllers/SourcesController.cs`, `Security/ClaimsPrincipalExtensions.cs`
- **Tests:** `EduScroll.Domain.Tests/SourceTests.cs`, `EduScroll.Application.Tests/Sources/SourceServiceTests.cs`, `EduScroll.API.IntegrationTests/WebExtraction/WebExtractionSecurityTests.cs`, `EduScroll.API.IntegrationTests/Sources/SourcesEndpointsIntegrationTests.cs`

### Created — Flutter (`mobile/`) & Docs (`docs/`)
- **Router Shell:** `mobile/lib/app/router/main_shell_screen.dart`
- **Source Feature (`mobile/lib/features/sources/` — 27 Dart files):** `domain/entities/*` (7), `domain/repositories/source_repository.dart`, `domain/services/*` (2), `data/models/*` (5), `data/services/*` (4), `data/repositories/source_repository_impl.dart`, `presentation/controllers/*` (6), `presentation/providers/source_providers.dart`, `presentation/screens/*` (5), `presentation/widgets/*` (2)
- **Tests:** `mobile/test/unit/source_validation_and_pdf_extractor_test.dart`, `mobile/test/unit/source_models_and_repository_test.dart`, `mobile/test/unit/source_controllers_test.dart`, `mobile/test/widget/sources_flow_widget_test.dart`, `mobile/test/integration/real_backend_sources_test.dart`
- **Report:** `docs/SPRINT-4-REPORT.md`

### Modified
- **Backend:** `EduScroll.API/Controllers/AuthController.cs`, `EduScroll.API/ErrorHandling/GlobalExceptionHandler.cs`, `EduScroll.API/appsettings.json`, `appsettings.Example.json`, `appsettings.Testing.json`, `EduScroll.Application/DependencyInjection.cs`, `EduScroll.Application/Exceptions/AppErrorKind.cs`, `EduScroll.Infrastructure/DependencyInjection.cs`, `EduScroll.Infrastructure/EduScroll.Infrastructure.csproj`, `EduScroll.Infrastructure/Persistence/ApplicationDbContext.cs`, `ApplicationDbContextModelSnapshot.cs`, `DatabaseMigrationAndReadinessTests.cs`
- **Flutter:** `mobile/pubspec.yaml`, `mobile/pubspec.lock`, `mobile/lib/app/app.dart`, `mobile/lib/app/router/app_router.dart`, `mobile/lib/app/theme/app_colors.dart`, `mobile/lib/app/theme/app_theme.dart`, `mobile/lib/core/constants/api_endpoints.dart`, `mobile/lib/core/error/app_failure.dart`, `mobile/lib/core/error/error_mapper.dart`, `mobile/lib/features/home/presentation/screens/home_screen.dart`
- **Docs:** `README.md`, `mobile/README.md`, `docs/03-architecture/flutter-architecture.md`

---

## 12. Known Issues

- **Scanned / Image-Only PDFs:** OCR is intentionally out of scope; PDFs without a selectable text layer return `PDF_NO_EXTRACTABLE_TEXT` with an honest explanation.
- **JavaScript-Rendered SPAs & Paywalled Pages:** `SecureWebContentFetcher` fetches server-rendered HTML over HTTP/HTTPS only; it does not execute page JavaScript or bypass logins/paywalls/CAPTCHAs.
- **Interactive Mobile Device / Emulator Run:** No Android emulator or physical device was booted locally (iOS requires macOS/Xcode); native Android/iOS file-picker UX should be manually smoke-tested when a device/emulator is connected. Android APK, Windows desktop, and Web builds all succeeded.
- **`syncfusion_flutter_pdf` Licensing Note:** Uses Syncfusion's free Community License (for eligible individuals/small teams) or commercial license; isolated behind `PdfTextExtractorService` for easy substitution if licensing requirements change.

---

## 13. Run Instructions

```powershell
# 1. Start SQL Server LocalDB & ensure Sprint 4 migration is applied
sqllocaldb start MSSQLLocalDB
cd backend
dotnet tool restore
dotnet ef database update --project src/EduScroll.Infrastructure/EduScroll.Infrastructure.csproj --startup-project src/EduScroll.API/EduScroll.API.csproj

# 2. Start Backend API (HTTP dev profile on localhost:5221)
dotnet run --project src/EduScroll.API/EduScroll.API.csproj --launch-profile http

# 3. Run Flutter App (Web, Windows, or Android Emulator)
cd ../mobile
flutter pub get
flutter run -d chrome --dart-define=APP_ENV=development --dart-define=API_BASE_URL=http://localhost:5221
# or: flutter run -d windows
# or (Android emulator — first set backend $env:AllowedHosts="localhost;127.0.0.1;10.0.2.2" & --urls http://0.0.0.0:5221):
# flutter run

# 4. Run Automated Tests
cd ../backend; dotnet test EduScroll.sln
cd ../mobile; flutter analyze; flutter test
```

---

## 14. Next Sprint Recommendation

**Sprint 5 — AI Content Analysis & Topic/Query Extraction:**
- Introduce `IAiAnalysisProvider` abstraction in `EduScroll.Application` with a local Ollama implementation (plus prompt-injection delimiters and strict JSON schema validation per `ADR-003` and `SEC-009`/`SEC-010`).
- Persist `SourceAnalysis`, `Topic`, and `SearchQuery` entities tied to owned `Source` records via SQL Server migrations, and introduce asynchronous source processing states (`ADR-004`).
