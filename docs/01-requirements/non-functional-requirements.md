# EduScroll — Non-Functional, Security, Privacy & Business Rules

- **Document Version:** 0.1
- **Status:** Draft — Pending Product Owner Review & Approval
- **Work Package:** WP-03 (Requirements Engineering)
- **Last Updated:** 2026-11-17

> **Rule Compliance:** No numerical performance targets, coverage quotas, or retention periods are invented. All unconfirmed thresholds are explicitly marked `TBD-*` in [PROJECT-DECISIONS.md](file:///C:/Users/Obad/Desktop/EduScroll/docs/PROJECT-DECISIONS.md).

---

## 1. Non-Functional Requirements (`NFR-*`)

| ID | Category | Classification | Requirement Statement | Acceptance / Verification Approach |
|---|---|---|---|---|
| **NFR-001** | Performance & Latency | **CONFIRMED** | Synchronous API endpoints (authentication, source status query, feed retrieval) shall respond within `TBD-010` under expected MVP concurrency. | Automated API load/latency tests once `TBD-010` thresholds are approved. |
| **NFR-002** | Asynchronous Processing | **PROPOSED** | Long-running ingestion/AI/discovery pipelines shall execute without blocking synchronous user navigation and shall complete within `TBD-010` end-to-end bounds (`DEC-P04`). | Integration and end-to-end timing checks against `TBD-010` bounds. |
| **NFR-003** | Reliability & Resilience | **CONFIRMED** | External integrations (web URL fetch, AI provider calls, YouTube Data API calls) shall enforce explicit request timeouts, bounded retries with backoff for transient faults, and graceful degradation. | Fault-injection integration tests simulating timeouts, HTTP 429, and 5xx responses (Constraint #13). |
| **NFR-004** | Availability & Recovery | **CONFIRMED** | Production deployment shall meet target uptime (`TBD-011`) and PostgreSQL backup/restore RPO/RTO (`TBD-011`). | Deployment health checks (`/health`) and backup restoration drills in Sprint 12. |
| **NFR-005** | Maintainability (Clean Architecture) | **CONFIRMED** | Backend projects shall strictly enforce Clean Architecture dependency rules: `Domain` has zero dependencies on `Application`, `Infrastructure`, or `API/Web`; `Application` depends only on `Domain`; `API` controllers contain no business logic. | Architecture unit tests (e.g., NetArchTest or build-graph checks) and code review (Constraints #9, #10). |
| **NFR-006** | AI Provider Portability | **CONFIRMED** | Replacing Ollama with an alternative LLM provider shall require adding/swapping an `Infrastructure` implementation and configuration without modifying `Domain` or `Application` use cases. | Provider-swap integration test with mock/alternate provider implementation (Constraint #7). |
| **NFR-007** | Testability | **CONFIRMED** | All Domain rules, Application use cases, AI output validators, SSRF validators, and ranking logic shall be unit-testable in isolation without live PostgreSQL, Ollama, or YouTube network access. | Automated unit test suite running offline in CI (Constraint #14). |
| **NFR-008** | Observability | **PROPOSED** | Backend services shall emit structured logs with correlation IDs across API requests, background processing, AI calls, and YouTube API calls, excluding sensitive user source text and secrets (`SEC-012`). | Log inspection in staging ensuring trace correlation and absence of PII/secrets/full document text. |
| **NFR-009** | Quota & Resource Efficiency | **CONFIRMED** | Video discovery shall minimize redundant YouTube Data API quota consumption via per-source query budgets (`TBD-014`), deduplication, and policy-compliant metadata caching (`TBD-015`). | API call count assertions per source pipeline run (`WP-07`). |
| **NFR-010** | Platform Compatibility | **CONFIRMED** | The Flutter client shall support the approved target platforms and minimum OS versions (`TBD-003`). | Device/emulator test matrix execution in Sprint 9–11. |

---

## 2. Security Requirements (`SEC-*`)

| ID | Domain | Classification | Requirement Statement | Verification Approach |
|---|---|---|---|---|
| **SEC-001** | Authentication | **CONFIRMED** | All non-public API endpoints (everything except registration, login, token refresh, health check) shall require a valid, signature-verified, non-expired JWT Bearer token. | API integration tests verifying `401 Unauthorized` for missing, tampered, or expired JWTs. |
| **SEC-002** | Password Security | **CONFIRMED** | User passwords shall be hashed and verified exclusively via ASP.NET Core Identity's salted adaptive password hasher and never logged or stored in plaintext. | Identity configuration audit and database inspection. |
| **SEC-003** | Authorization & Isolation | **CONFIRMED** | Every user-owned resource (sources, analyses, source feeds, bookmarks/history if enabled) shall enforce server-side ownership checks comparing the authenticated JWT subject (`sub`) against the resource owner ID. | Automated IDOR (Insecure Direct Object Reference) tests attempting cross-user read/retry/delete (`Constraint #12`). |
| **SEC-004** | Transport Security | **CONFIRMED** | All client-to-backend communication in production shall use HTTPS/TLS; HTTP shall be redirected or rejected. | Transport configuration verification and staging TLS check. |
| **SEC-005** | Client Credential Storage | **CONFIRMED** | Flutter shall store JWT access/refresh tokens only in OS-backed encrypted secure storage (Keychain/Keystore), never in plaintext preferences or logs. | Flutter code audit and device storage inspection. |
| **SEC-006** | SSRF Protection | **CONFIRMED** | Backend URL acquisition shall validate schemes (`http`/`https` only), resolve DNS prior to connection, block private/loopback/link-local/multicast/cloud-metadata IP ranges (IPv4 & IPv6), enforce connection-time IP pinning or re-validation on every HTTP redirect, and enforce strict timeouts and response size caps (`TBD-005`). | Unit and integration tests against `localhost`, `127.0.0.1`, `::1`, `169.254.169.254`, RFC1918/RFC4193 ranges, and DNS-rebinding/redirect scenarios (Constraint #6). |
| **SEC-007** | Input Validation | **CONFIRMED** | Backend endpoints shall validate payload sizes (`TBD-005`), character encodings, URL syntax, and content types at the API/Application boundary before processing. | Negative API tests with oversized, empty, binary, and malformed payloads. |
| **SEC-008** | Secrets Management | **CONFIRMED** | YouTube Data API keys, JWT signing keys, database credentials, and LLM provider keys shall never be bundled in Flutter binaries or committed to Git; they must be injected via environment variables or a secret manager. | Static secret scanning in CI and Flutter build artifact inspection (Constraint #11). |
| **SEC-009** | AI Prompt Injection Defense | **CONFIRMED** | Untrusted source text and YouTube metadata passed into AI prompts shall be delimited from system instructions, and AI models shall never be granted tool/SQL/shell execution privileges. | Adversarial prompt-injection test fixtures verifying instructions inside PDF/URL text cannot alter schema or execute commands (`WP-06`, `WP-09`). |
| **SEC-010** | Untrusted AI Output Validation | **CONFIRMED** | All AI responses shall be treated as untrusted input, strictly parsed and validated against JSON schema, length/count bounds, and safe text rules before storage or query execution. | Unit tests with malformed JSON, missing fields, oversized arrays, and script/markup payloads (Constraint #8). |
| **SEC-011** | Rate Limiting & Abuse Prevention | **CONFIRMED** | The API shall enforce per-IP and/or per-user rate limits (`TBD-021`) on authentication, source submission, and video discovery triggers to prevent resource and YouTube quota exhaustion. | Integration tests exceeding thresholds and verifying `429 Too Many Requests`. |

---

## 3. Privacy Requirements (`SEC-012`–`SEC-015`)

| ID | Domain | Classification | Requirement Statement | Verification Approach |
|---|---|---|---|---|
| **SEC-012** | Log Hygiene | **CONFIRMED** | Application logs and error reports shall not record raw user passwords, JWT/refresh tokens, API keys, or full extracted PDF/URL source text. | Automated log scrubbing review and test assertions on log sinks. |
| **SEC-013** | Data Minimization | **CONFIRMED** | Raw PDF binary files shall not be transmitted to or stored by the backend in MVP (Constraint #3–#4); only required extracted text and derived metadata are processed. | Network payload inspection verifying PDF submission transmits text, not PDF file bytes. |
| **SEC-014** | Data Retention & Deletion | **CONFIRMED** | User-submitted sources, analyses, and account data shall be retained only for approved retention periods (`TBD-019`) and deleted/purged upon user source or account deletion request (`TBD-019`, `TBD-020`). | Integration tests verifying cascading/soft-to-hard deletion lifecycle per `TBD-019`. |
| **SEC-015** | External AI Data Disclosure | **PROPOSED** | If a hosted third-party AI provider is approved for production (`ADR-003`, `TBD-020`), users shall be informed via privacy notice before user-submitted text leaves operator-controlled infrastructure. | Privacy policy review prior to Sprint 12 launch. |

---

## 4. Business Rules (`BR-*`)

| ID | Classification | Business Rule | Source / Constraint |
|---|---|---|---|
| **BR-001** | **CONFIRMED** | EduScroll shall store only YouTube video identifiers, metadata, and discovery associations—never downloaded or hosted video/audio streams. | Constraint #1 |
| **BR-002** | **CONFIRMED** | Video playback in EduScroll shall only play videos marked embeddable/available via YouTube Data API and must display YouTube's required player branding and controls. | Constraint #2 |
| **BR-003** | **CONFIRMED** | In the initial MVP, PDF text extraction occurs strictly on the user's Flutter client; the backend accepts extracted text rather than raw PDF uploads. | Constraints #3, #4 |
| **BR-004** | **CONFIRMED** | Web URL fetching occurs exclusively on the backend (never delegated to client scraping) and only over publicly routable HTTP/HTTPS endpoints passing SSRF validation. | Constraints #5, #6 |
| **BR-005** | **CONFIRMED** | No AI-generated analysis or search query may be persisted or dispatched to YouTube until it passes schema and safety validation. | Constraint #8 |
| **BR-006** | **CONFIRMED** | Every source, analysis, and source-specific feed belongs to exactly one authenticated user unless an explicit sharing/caching policy is approved (`TBD-023`). | Constraint #12 |
| **BR-007** | **CONFIRMED** | Business rules and domain invariants reside exclusively in `Domain` and `Application` layers, never in API controllers or Flutter UI widgets. | Constraints #9, #10 |
| **BR-008** | **CONFIRMED** | YouTube API keys and AI provider secrets reside exclusively on the backend and must never exist in client code or configuration. | Constraint #11 |
