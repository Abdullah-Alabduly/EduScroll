# EduScroll — Project Decisions, Conflicts, Assumptions & TBD Register

- **Document Version:** 0.1
- **Status:** Draft — Pending Product Owner Review & Approval
- **Work Package:** Cross-Cutting Governance (Step A/B, Section 4, WP-01–WP-11)
- **Last Updated:** 2026-11-17

> **Classification Legend**
> - **CONFIRMED:** Explicitly established in the Sprint 0 project brief or directly observed in the repository.
> - **PROPOSED:** Engineering recommendation requiring explicit Product Owner approval before implementation.
> - **ASSUMPTION — Requires Confirmation:** Reasonable working premise needed for analysis; must be confirmed or replaced by the Product Owner.
> - **TBD — Requires Product Owner Decision:** Unknown requirement, threshold, or design parameter; must not be invented.

---

## 1. Repository Inspection Findings & Conflicts (Step A)

Inspection of `c:\Users\Obad\Desktop\EduScroll` (commit `4b3ddda202ebede99094fd05d743f3ad85a70b74`, `"chore: initialize EduScroll solution architecture"`) revealed an existing .NET 10 solution (`EduScroll.slnx`) and four C# projects, with no existing `docs/` folder, no existing SRS/ADR files, an empty `test/` directory, and no Flutter project.

| ID | Finding / Conflict | Evidence Observed in Repository | Conflict with Sprint 0 Brief | Resolution Status |
|---|---|---|---|---|
| **CONF-001** | `EduScroll.Web` is scaffolded as ASP.NET Core MVC (Razor Views, Bootstrap, jQuery), not a REST Web API. | `src/EduScroll.Web/Program.cs` (`AddControllersWithViews()`, `MapControllerRoute`), `Controllers/HomeController.cs`, `Views/`, `wwwroot/lib/`. | Brief Section 2 specifies `ASP.NET Core 10 Web API` and `REST API` communication with a `Flutter` frontend. | **Unresolved** — See `ADR-001` (`TBD-001`). **PROPOSED:** Convert/replace `EduScroll.Web` with a pure API entry point (`EduScroll.Api` or API-only `EduScroll.Web`) in Sprint 1. |
| **CONF-002** | `IPdfTextExtractor` exists in Backend Application layer. | `src/EduScroll.Application/Interfaces/Pdf/IPdfTextExtractor.cs` (`ExtractAsync(Stream PdfStream, CancellationToken)`). | Brief Section 3 Constraint #3 & #4 state PDF text extraction happens locally in Flutter and extracted text is sent to the backend. | **Unresolved** — See `ADR-002` (`TBD-002`). **PROPOSED:** Preserve Constraint #3/#4 (Flutter extracts text locally) and remove/deprecate `IPdfTextExtractor` in Sprint 1 unless backend fallback is explicitly approved. |
| **CONF-003** | Malformed `Ollama` configuration nested inside `ConnectionStrings` in `appsettings.json`. | `src/EduScroll.Web/appsettings.json` lines 8–14 place `"Ollama": { "BaseUrl": "http://localhost:11434", "Model": "YOUR_MODEL_NAME", "TimeoutSeconds": 120 }` inside `"ConnectionStrings"`. | ASP.NET Core `ConnectionStrings` expects key-to-string pairs; structured objects belong in a top-level section (e.g., `"AiProvider"` or `"Ollama"`). Also contains placeholder `YOUR_MODEL_NAME`. | **Unresolved** — Flagged for Sprint 1 cleanup; model selection recorded as `TBD-012`. |
| **CONF-004** | Empty `test/` directory and no test projects in `EduScroll.slnx`. | `EduScroll.slnx` lists only `src/*` projects; `test/` is empty. | Sprint 0 prohibits writing code; test projects must be introduced in Sprint 1+. | **CONFIRMED** — Covered in [testing-strategy.md](file:///C:/Users/Obad/Desktop/EduScroll/docs/08-quality/testing-strategy.md) and Sprint 1 roadmap. |

---

## 2. Confirmed Decisions (From Project Brief)

| ID | Decision | Source |
|---|---|---|
| **DEC-C01** | Product name is **EduScroll**: AI-powered educational short-video discovery application transforming PDFs and public web URLs into a vertical short-video discovery feed. | Brief §1.1, §1.2 |
| **DEC-C02** | Intended primary 11-step user journey (account -> submit PDF/URL -> extract/obtain text -> backend process -> AI analysis -> query gen -> YouTube discovery -> relevance ranking -> metadata storage -> video feed -> watch & revisit sources). | Brief §1.3 |
| **DEC-C03** | Intended technical direction: ASP.NET Core 10 Web API, Clean Architecture, Flutter, PostgreSQL, EF Core, ASP.NET Core Identity + JWT, AI Provider Abstraction, initial local AI provider Ollama, YouTube Data API, REST API, production-oriented deployment with CI/CD. | Brief §2 |
| **DEC-C04** | System stores video metadata and references only; never downloads or hosts YouTube video files. | Brief §3 #1 |
| **DEC-C05** | Video playback must respect applicable YouTube API policies, branding requirements, and embedding restrictions. | Brief §3 #2 |
| **DEC-C06** | PDF text extraction occurs locally in Flutter for the initial MVP workflow; extracted text is sent to backend. | Brief §3 #3, #4 |
| **DEC-C07** | Web URL content acquisition occurs on backend (or dedicated backend service) with SSRF protection blocking internal networks, localhost, cloud metadata endpoints, and restricted resources. | Brief §3 #5, #6 |
| **DEC-C08** | AI integration uses a provider abstraction; all AI output is validated before acceptance. | Brief §3 #7, #8 |
| **DEC-C09** | Clean Architecture boundaries: no business logic in controllers; Domain does not depend on Infrastructure. | Brief §3 #9, #10 |
| **DEC-C10** | Secrets and API keys must never be embedded in Flutter. | Brief §3 #11 |
| **DEC-C11** | User data isolated and protected by ownership and authorization rules. | Brief §3 #12 |
| **DEC-C12** | System handles failures, timeouts, retries, and incomplete processing; architecture supports testing and maintenance; MVP remains focused without enterprise over-engineering. | Brief §3 #13, #14, #15 |

---

## 3. Proposed Decisions Awaiting Product Owner Approval

| ID | Topic | Proposed Recommendation | Alternatives Considered | ADR / Doc Reference | Status |
|---|---|---|---|---|---|
| **DEC-P01** | `EduScroll.Web` Project Type | Refactor `EduScroll.Web` (or replace with `EduScroll.Api`) into a pure ASP.NET Core 10 Web API without Razor Views/Bootstrap/jQuery. | Keep MVC + add API controllers; separate API project alongside Web. | [ADR-001](file:///C:/Users/Obad/Desktop/EduScroll/docs/03-architecture/adr/ADR-001-web-api-vs-mvc.md) | Pending Approval |
| **DEC-P02** | PDF Extraction Boundary | Retain local extraction in Flutter (Constraint #3/#4); remove/defer backend `IPdfTextExtractor` unless scanned-PDF/server-fallback is added post-MVP. | Move extraction to backend using `IPdfTextExtractor`; support dual path in MVP. | [ADR-002](file:///C:/Users/Obad/Desktop/EduScroll/docs/03-architecture/adr/ADR-002-pdf-extraction-boundary.md) | Pending Approval |
| **DEC-P03** | AI Provider Strategy (Dev vs Prod) | Use Ollama for local development/testing via `IAiAnalysisProvider` abstraction, and evaluate a hosted/managed LLM API option (or dedicated GPU host) for production deployment before Sprint 12. | Ollama-only in production; hosted-only from Sprint 5. | [ADR-003](file:///C:/Users/Obad/Desktop/EduScroll/docs/03-architecture/adr/ADR-003-ai-provider-deployment.md) | Pending Approval |
| **DEC-P04** | Source Processing Execution Model | Asynchronous background processing with job/source status states (`Pending`, `AcquiringContent`, `Analyzing`, `DiscoveringVideos`, `Ranking`, `Completed`, `PartiallyCompleted`, `Failed`) and client status polling for MVP. | Synchronous HTTP request blocking until AI + YouTube complete; WebSockets/SignalR push. | [ADR-004](file:///C:/Users/Obad/Desktop/EduScroll/docs/03-architecture/adr/ADR-004-async-processing-and-status.md) | Pending Approval |
| **DEC-P05** | Flutter State Management | Adopt **Riverpod** (or **BLoC/Cubit** as runner-up) for testable, explicit async state management across auth, ingestion, and feed features. | Provider; BLoC/Cubit; GetX. | [ADR-005](file:///C:/Users/Obad/Desktop/EduScroll/docs/03-architecture/adr/ADR-005-flutter-state-management.md) | Pending Approval |
| **DEC-P06** | YouTube Playback Mechanism in Flutter | Use an official/policy-compliant embedded YouTube player mechanism (WebView-based YouTube IFrame Player API wrapper) respecting branding, controls, and `embeddable` flag—never raw stream extraction. | Launch external YouTube app/browser only; third-party stream extractors (violates ToS). | [ADR-006](file:///C:/Users/Obad/Desktop/EduScroll/docs/03-architecture/adr/ADR-006-youtube-playback-embedding.md) | Pending Approval |
| **DEC-P07** | Error Response Format | Standardize REST error responses on RFC 9457 `ProblemDetails` with machine-readable error codes and correlation IDs. | Custom JSON error envelope. | [integration-design.md](file:///C:/Users/Obad/Desktop/EduScroll/docs/03-architecture/integration-design.md) | Pending Approval |
| **DEC-P08** | API Versioning | Introduce URL path versioning (`/api/v1/...`) from Sprint 1. | Header versioning; unversioned MVP routes. | [integration-design.md](file:///C:/Users/Obad/Desktop/EduScroll/docs/03-architecture/integration-design.md) | Pending Approval |
| **DEC-P09** | MVP User Roles | Single authenticated end-user role (`Learner` / standard user) for MVP; no admin portal or multi-tier roles unless requested. | Add Admin/Moderator role in MVP. | [stakeholders.md](file:///C:/Users/Obad/Desktop/EduScroll/docs/00-product/stakeholders.md) | Pending Approval |
| **DEC-P10** | Token Strategy | Short-lived JWT access token + revocable/rotating refresh token stored in Flutter secure storage. | Long-lived access token only; cookie auth. | [integration-design.md](file:///C:/Users/Obad/Desktop/EduScroll/docs/03-architecture/integration-design.md) | Pending Approval |

---

## 4. Working Assumptions (`ASSUMPTION — Requires Confirmation`)

| ID | Assumption | Impact if Wrong |
|---|---|---|
| **ASM-001** | Users are single-tier individual learners; no organizational/tenant hierarchy or paid subscription tiers exist in MVP. | Data model, auth claims, and UI would require tenant/role/billing boundaries. |
| **ASM-002** | The MVP targets Android and iOS mobile clients via Flutter (Web/Desktop Flutter secondary or deferred). | Flutter PDF extraction and YouTube IFrame WebView plugins behave differently on Web/Desktop. |
| **ASM-003** | MVP processes text-based PDFs (searchable text layer), not scanned image-only PDFs requiring OCR. | OCR requires either on-device ML/OCR or backend file upload + OCR engine, altering Constraint #3/#4 and storage/privacy. |
| **ASM-004** | Initial MVP focuses on single-source-to-feed generation (and viewing per-source or unified user feed), without collaborative sharing or social features. | Social sharing requires public/shared access models and moderation. |
| **ASM-005** | YouTube Data API v3 is used under standard Google Cloud API key/project credentials owned by the backend operator, not per-user YouTube OAuth. | Per-user OAuth changes auth flows, token storage, and quota attribution. |
| **ASM-006** | "Short-video" means videos suited to vertical short-form consumption (e.g., YouTube Shorts or concise educational clips), though exact duration bounds remain `TBD-016`. | Affects search filters (`videoDuration`) and player aspect-ratio UI. |
| **ASM-007** | Initial target language(s) will be specified by Product Owner; architecture should avoid hardcoding English-only assumptions where UTF-8 suffices, while prompt/search language rules remain `TBD-004`. | Non-Latin scripts affect tokenization, chunking, and YouTube `relevanceLanguage`. |

---

## 5. Unresolved Decisions & Missing Requirements (`TBD — Requires Product Owner Decision`)

| ID | Category | Unresolved Question / Missing Specification | Blocking Sprint |
|---|---|---|---|
| **TBD-001** | Architecture | Confirm resolving `CONF-001`: convert `EduScroll.Web` to pure Web API (and remove MVC views/Bootstrap/jQuery) or keep separate projects. | Sprint 1 |
| **TBD-002** | Architecture | Confirm resolving `CONF-002`: remove `IPdfTextExtractor` from backend in favor of Flutter-local extraction (Constraint #3), or retain as a server fallback. | Sprint 1 / 4 |
| **TBD-003** | Product / Scope | Define target mobile platforms for MVP (Android only, iOS only, or both; Flutter Web/Desktop in/out). | Sprint 3 |
| **TBD-004** | Product / AI | Define supported content & UI languages (e.g., English only, Arabic + English, multilingual). | Sprint 5 |
| **TBD-005** | Ingestion / Limits | Define maximum PDF file size (client-side), maximum extracted text character/token length accepted by API, and maximum URL HTML payload size. | Sprint 4 |
| **TBD-006** | Ingestion / PDF | Define behavior for scanned/image-only PDFs (reject with clear error vs. OCR) and password-protected PDFs. | Sprint 4 |
| **TBD-007** | Ingestion / URL | Define URL ingestion rules for paywalled, login-gated, JS-rendered (SPA), or non-HTML (e.g., direct `.pdf` URL) links. | Sprint 4 |
| **TBD-008** | Auth & Identity | Define registration verification rules (email confirmation required or immediate login?), password policy beyond Identity defaults, and password reset mechanism. | Sprint 2 |
| **TBD-009** | Auth / Tokens | Define JWT access token lifetime, refresh token lifetime, and concurrent device/session policy. | Sprint 2 |
| **TBD-010** | NFR / Performance | Define measurable latency/throughput targets (API auth/feed response time, acceptable end-to-end source processing time). | Sprint 8 / 11 |
| **TBD-011** | NFR / Availability | Define uptime/availability expectations, backup RPO/RTO, and target deployment environment/cloud provider. | Sprint 12 |
| **TBD-012** | AI / Model | Select specific Ollama model(s) for local dev/eval and confirm production LLM hosting strategy (self-hosted GPU vs. managed cloud LLM provider). | Sprint 5 / 12 |
| **TBD-013** | AI / Processing | Define handling of very long documents exceeding model context window (truncate vs. map-reduce chunking vs. reject above threshold). | Sprint 5 |
| **TBD-014** | Video Discovery | Define number of search queries generated per source, candidate videos fetched per query, and final feed size per source. | Sprint 6 / 7 |
| **TBD-015** | Video Discovery | Confirm YouTube API quota budget, daily user submission/refresh rate limits, and caching TTL compliant with YouTube API Terms of Service. | Sprint 6 |
| **TBD-016** | Ranking / Short Video | Define "short-video" duration constraints and filtering criteria (language, captions, embeddability, safety filters). | Sprint 7 |
| **TBD-017** | Feed & UX | Define feed composition: per-source feed only, combined cross-source "My Feed", or both; and whether users can trigger re-discovery/refresh. | Sprint 9 |
| **TBD-018** | Feed & UX | Confirm whether Bookmarking (`FR-027`) and Viewing History (`FR-028`) are Must Have (MVP) or Should Have (Sprint 10/post-MVP). | Sprint 9 / 10 |
| **TBD-019** | Data & Privacy | Define retention periods for extracted source text, raw/cleaned text, analysis outputs, user viewing history, and account deletion cascade/SLA. | Sprint 2 / 8 |
| **TBD-020** | Privacy / Compliance | Confirm applicable privacy regulations based on target launch geography (e.g., GDPR, CCPA) and whether source text may be sent to external hosted LLM APIs. | Sprint 5 / 11 |
| **TBD-021** | Security | Confirm rate-limiting thresholds per IP/user for auth, source submission, and feed endpoints. | Sprint 2 / 11 |
| **TBD-022** | Flutter Stack | Approve Flutter state management choice (`ADR-005`), navigation package, HTTP client, and local PDF extraction library candidates after Sprint 3 spike. | Sprint 3 |
| **TBD-023** | Data / Deduplication | Define whether two users submitting the same URL or identical text share cached YouTube metadata/analysis or remain strictly isolated per user (privacy vs. quota trade-off). | Sprint 6 / 8 |
| **TBD-024** | Feed / Revisit Source | Clarify step 11 ("revisit their sources"): view stored summary/topics + original URL link, or re-open local PDF/extracted text preview? | Sprint 9 / 10 |
