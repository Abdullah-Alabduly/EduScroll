# EduScroll — Testing & Quality Strategy (WP-10)

- **Document Version:** 0.1
- **Status:** Draft — Pending Product Owner Review & Approval
- **Work Package:** WP-10 (Testing & Quality Strategy)
- **Last Updated:** 2026-11-17

> **Rule Compliance:** No arbitrary code-coverage percentages or unapproved numerical performance targets are invented (`TBD-010` governs performance thresholds).

---

## 1. Testing Levels: Purpose & Scope

| Testing Level | Purpose | Scope & Tooling Direction (Unselected libs not assumed) |
|---|---|---|
| **Unit Testing** | Verify isolated business rules, invariants, validators, and scoring math deterministically and offline (`NFR-007`). | Domain state transitions; Application text cleaners, URL/SSRF IP validators, AI JSON schema validator, query sanitizer, deduplicator, ranking scorer; Flutter state controllers/mappers. |
| **Architecture Testing** | Prevent Clean Architecture erosion (`NFR-005`). | Automated assembly-reference tests ensuring `Domain` has no Infrastructure/API/EF dependencies and `Application` has no Infrastructure/API dependencies. |
| **Integration Testing** | Verify Infrastructure adapters against real or containerized dependencies. | EF Core + PostgreSQL migrations/queries/ownership filters (`Testcontainers` or local test DB); HTTP mock-handler tests for Ollama, YouTube API, and SSRF redirect/DNS-rebinding edge cases. |
| **API Testing** | Verify HTTP contracts, auth, validation, `ProblemDetails`, pagination, IDOR, and rate limiting. | ASP.NET Core `WebApplicationFactory` end-to-end HTTP tests covering `401`, `403`/`404` IDOR, `400`/`422`, `429`, and `202` async submission flows. |
| **Flutter Widget Testing** | Verify UI state rendering, form validation, navigation guards, and feed/player states without live backend. | Widget tests with mocked state/repositories for Auth screens, PDF/URL submission, status polling states, vertical feed scrolling, and error/empty views. |
| **End-to-End (E2E) Testing** | Validate full user journeys across Flutter + Backend + PostgreSQL (+ test doubles or staging Ollama/YouTube). | Registration/Sign-In -> PDF & URL submission -> status completion -> feed retrieval -> playback & source revisit (`WF-01`–`WF-15`). |
| **Security Testing** | Verify `SEC-001`–`SEC-015` threat mitigations (`initial-threat-analysis.md`). | IDOR matrix, SSRF bypass payloads, prompt-injection fixtures, secret scanning in CI + Flutter build inspection, log hygiene checks, TLS/header verification. |
| **Performance Testing** | Measure latency, throughput, and memory/quota behavior under realistic load against `TBD-010`. | API load tests (feed/auth endpoints), pipeline timing benchmarks (Ollama vs. hosted), Flutter memory profiling during PDF extraction and vertical WebView scrolling. |
| **AI Output Evaluation** | Measure schema compliance, grounding, and prompt-injection resistance across candidate models (`evaluation-strategy.md`). | Offline golden dataset runner executed before model/prompt changes (`TBD-012`). |
| **Ranking Quality Evaluation** | Verify relevance scoring effectiveness empirically (`ranking-strategy.md`). | Labeled `(SourceAnalysis, CandidateVideos)` dataset measuring Precision@K / NDCG@K before tuning/shipping weights. |

---

## 2. Sprint-by-Sprint Testing Scope (Aligned to Roadmap `WP-11`)

| Sprint | Primary Test Activities |
|---|---|
| **S1 — Backend Foundation** | Domain/Application isolation tests, Architecture dependency tests (`NFR-005`), health endpoint, OpenAPI, CORS, host-filtering & `ProblemDetails` integration tests (`EduScroll.API.IntegrationTests`). *(CI pipeline & secret-scan workflow deferred until remote repository/CI host is connected.)* |
| **S2 — Database & Authentication** | PostgreSQL migration & repository integration tests; Identity registration/login/refresh/revocation API tests; `401`/rate-limit security checks. |
| **S3 — Flutter Foundation** | Flutter widget tests for auth state & routing guards; secure storage integration spike; CI Flutter test/analyze pipeline. |
| **S4 — Source Ingestion & Extraction** | Flutter local PDF extraction tests (valid, empty, oversized, encrypted); backend SSRF unit/integration test suite (loopback, RFC1918, metadata, redirects); URL/text validation API tests. |
| **S5 — AI Content Analysis** | `OutputValidator` unit tests (malformed JSON, oversized arrays, XSS); prompt-injection security tests; Ollama adapter HTTP mock tests; initial golden dataset AI evaluation (`evaluation-strategy.md`). |
| **S6 — Video Discovery Engine** | YouTube API adapter mock tests (`search.list` + `videos.list`), deduplication & `embeddable` filtering unit tests, quota-exhaustion (`403`) & retry fault-injection tests. |
| **S7 — Relevance & Ranking Engine** | Ranking signal unit tests (including missing metadata edge cases) + labeled dataset ranking quality evaluation (`ranking-strategy.md`). |
| **S8 — Core Pipeline Integration** | Full backend async pipeline integration tests (`Pending` -> `Completed`/`PartiallyCompleted`/`Failed`), retry (`WF-15`) & transaction rollback tests, IDOR tests across source/feed endpoints. |
| **S9 — Educational Feed & Video Experience** | Flutter feed pagination widget tests, embedded YouTube player lifecycle/fallback tests (`WF-13`), E2E source-to-feed-to-revisit smoke tests. |
| **S10 — Complete User Experience** | Source library/delete tests, error-recovery UI tests, plus conditional Bookmark/History/Password-Reset tests (`TBD-018`, `TBD-008`). |
| **S11 — Quality Assurance & Security** | Full regression E2E suite, security/penetration verification (`THR-001`–`THR-013`), log hygiene audit, YouTube ToS compliance review, performance/load tests against `TBD-010`. |
| **S12 — Deployment, Beta & Launch** | Staging smoke tests, DB backup/restore drill (`NFR-004`), post-deploy synthetic health checks, beta telemetry/error monitoring verification. |
