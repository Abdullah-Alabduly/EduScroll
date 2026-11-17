# EduScroll — Functional Requirements

- **Document Version:** 0.1
- **Status:** Draft — Pending Product Owner Review & Approval
- **Work Package:** WP-03 (Requirements Engineering)
- **Last Updated:** 2026-11-17

> **Rule Compliance:** No numerical thresholds (file sizes, character counts, feed sizes, timeouts, or token lifetimes) are invented. Missing thresholds reference `TBD-*` in [PROJECT-DECISIONS.md](file:///C:/Users/Obad/Desktop/EduScroll/docs/PROJECT-DECISIONS.md).

---

## 1. Authentication & Session Management

| ID | Classification | Requirement Statement | Rationale / Goal | Acceptance Criteria |
|---|---|---|---|---|
| **FR-001** | **CONFIRMED** | The system shall allow an unauthenticated user to register a new account using a unique identifier (email/username) and password via ASP.NET Core Identity. | Journey Step 1; `PG-004` | Valid credentials create a persisted user account; duplicate identifier returns a conflict error; verification behavior follows `TBD-008`. |
| **FR-002** | **CONFIRMED** | The system shall authenticate registered users and issue a signed JWT access token upon valid sign-in credentials. | Journey Step 1; `PG-004` | Valid credentials return an access token (and refresh token if `DEC-P10` approved) within `TBD-010`; invalid credentials return authentication failure without leaking account existence. |
| **FR-003** | **PROPOSED** | The system shall allow an authenticated user to refresh an expired access token using a valid, non-revoked refresh token (`DEC-P10`). | Session continuity; `PG-004` | Valid refresh token issues a new access token and rotates/validates the refresh token per `TBD-009`; revoked or expired refresh token is rejected. |
| **FR-004** | **PROPOSED** | The system shall allow an authenticated user to sign out, clearing client-side stored tokens and revoking the active refresh token on the backend. | Session termination; `PG-004` | Client removes credentials from secure storage; subsequent use of revoked refresh token fails. |

---

## 2. Source Submission & Content Acquisition

| ID | Classification | Requirement Statement | Rationale / Goal | Acceptance Criteria |
|---|---|---|---|---|
| **FR-005** | **CONFIRMED** | The Flutter client shall allow an authenticated user to select a local PDF file (within `TBD-005` size bounds) and extract its text locally on the device prior to backend submission. | Constraints #3, #4; `PG-001` | Selecting a valid text-based PDF extracts text locally without uploading the raw PDF binary; exceeding `TBD-005` or failing extraction surfaces a clear error (`TBD-006`). |
| **FR-006** | **CONFIRMED** | The Flutter client shall transmit the locally extracted PDF text and minimal client-provided metadata (e.g., user-visible source title/filename) to the backend over HTTPS. | Constraint #4; `PG-001` | Backend receives UTF-8 text payload and associates the created source record strictly with the authenticated user. |
| **FR-007** | **CONFIRMED** | The system shall allow an authenticated user to submit a publicly accessible HTTP/HTTPS web page URL to the backend for content acquisition. | Journey Step 2; Constraint #5; `PG-001` | Valid `http`/`https` URL creates a source submission bound to the authenticated user; syntactically invalid or disallowed schemes are rejected immediately. |
| **FR-008** | **CONFIRMED** | The backend shall acquire web page content for submitted URLs while enforcing SSRF protections (`SEC-006`) and payload/timeout limits (`TBD-005`). | Constraints #5, #6; `PG-001`, `PG-004` | Public educational HTML pages are fetched and main textual content extracted; private IPs, localhost, metadata endpoints, and disallowed redirects are blocked and logged without leaking internal network details. |
| **FR-009** | **CONFIRMED** | The system shall validate and clean acquired source text (normalizing whitespace, stripping HTML boilerplate/scripts, checking minimum/maximum content bounds per `TBD-005`) before AI analysis. | Constraint #8, #13; `PG-001` | Empty, non-textual, or out-of-bounds content transitions the source to a rejected/failed validation state with an actionable error reason. |
| **FR-010** | **CONFIRMED** | The system shall persist the submitted source record (ownership, source type, title, source URL if applicable, validated/cleaned text per `TBD-019`, and processing state) in PostgreSQL. | Journey Step 9; `PG-001`, `PG-004` | Source record is persisted and retrievable only by its owning user. |

---

## 3. AI Content Analysis & Search Query Generation

| ID | Classification | Requirement Statement | Rationale / Goal | Acceptance Criteria |
|---|---|---|---|---|
| **FR-011** | **CONFIRMED** | The backend Application layer shall invoke AI content analysis exclusively through an AI provider abstraction interface (`IAiAnalysisProvider` or equivalent) decoupled from concrete provider implementations. | Constraint #7; `PG-005` | Switching between Ollama and another provider requires no changes to Domain or Application business logic. |
| **FR-012** | **CONFIRMED** | For valid source text, the AI analysis component shall extract structured educational metadata comprising: `topics`, `subtopics`, `keywords`, `concepts`, and a `summary`. | Journey Step 5; `PG-001` | Output conforms to the defined JSON schema (`WP-06`) and respects language rules (`TBD-004`) and context-window handling (`TBD-013`). |
| **FR-013** | **CONFIRMED** | The backend shall validate every AI-generated output against strict schema, type, completeness, and safety rules before persisting or using it downstream. | Constraint #8; `PG-001`, `PG-004` | Malformed, empty, off-schema, or prompt-injected outputs are rejected; bounded retry or failure state (`FR-025`) is triggered instead of persisting invalid data. |
| **FR-014** | **CONFIRMED** | The system shall generate bounded search queries derived from the validated topics, subtopics, keywords, and concepts (respecting `TBD-014` query count bounds). | Journey Step 6; `PG-002` | Each completed analysis produces at least one validated, non-empty search query persisted or passed to the discovery stage. |
| **FR-015** | **CONFIRMED** | The system shall persist the validated source analysis (summary, topics, subtopics, keywords, concepts) and generated search queries linked to the source. | Journey Step 9; `PG-001` | Persisted analysis is retrievable by the source owner for display and source revisiting (`FR-022`). |

---

## 4. Video Discovery, Ranking & Metadata Persistence

| ID | Classification | Requirement Statement | Rationale / Goal | Acceptance Criteria |
|---|---|---|---|---|
| **FR-016** | **CONFIRMED** | The video discovery component shall query the YouTube Data API from the backend using the generated search queries without exposing API keys to the client. | Journey Step 7; Constraint #11; `PG-002` | Queries execute server-side using backend-configured credentials; candidate video IDs and required metadata are retrieved within `TBD-014` and `TBD-015` bounds. |
| **FR-017** | **CONFIRMED** | The discovery component shall deduplicate candidate videos returned across multiple search queries for the same source and enrich metadata (including embeddability, duration, title, description, channel, thumbnails) as needed. | `WP-07`; `PG-002` | Duplicate YouTube video IDs appear at most once per source feed; non-embeddable or unavailable videos are filtered out per Constraint #2. |
| **FR-018** | **CONFIRMED** | The ranking component shall evaluate each candidate video for relevance against the source's analyzed topics, concepts, and keywords, and assign an ordered rank/score. | Journey Step 8; `PG-002` | Eligible candidate videos receive deterministic ranking scores (using approved signals per `WP-07`) and are ordered descending by score; filtering rules follow `TBD-016`. |
| **FR-019** | **CONFIRMED** | The system shall store only YouTube video metadata, YouTube video identifiers/references, and source-to-video ranking associations—never downloaded or re-hosted video/audio files. | Journey Step 9; Constraint #1; `PG-002` | Database and storage contain metadata/IDs/URLs only; no video media binaries are written or served. |

---

## 5. Educational Feed, Video Playback & Source Management

| ID | Classification | Requirement Statement | Rationale / Goal | Acceptance Criteria |
|---|---|---|---|---|
| **FR-020** | **CONFIRMED** | The backend shall provide paginated access to ranked discovered videos for a user's source (and/or unified personal feed per `TBD-017`), isolated to the authenticated owner. | Journey Step 10; Constraint #12; `PG-003` | Authenticated owner receives ordered feed pages containing video metadata, ranking context, and source reference; other users receive `403`/`404`. |
| **FR-021** | **CONFIRMED** | The Flutter client shall render discovered videos in a vertical short-video feed and play selected videos using a YouTube policy-compliant embedded player respecting branding and embedding restrictions. | Journey Step 10–11; Constraint #2; `PG-003` | Users can vertically scroll between feed items; playback uses an approved YouTube embedded player showing required YouTube branding/controls; unplayable/removed videos display graceful fallback state. |
| **FR-022** | **CONFIRMED** | The system shall allow an authenticated user to view a list of their submitted sources, inspect a source's analysis summary/topics and original URL (where applicable), and navigate from a feed video back to its originating source (`TBD-024`). | Journey Step 11; `PG-003`, `PG-004` | Owner can list sources with status, open source detail view from both library and video card, and delete a source (`FR-023`). |
| **FR-023** | **PROPOSED** | The system shall allow an authenticated user to delete any of their own submitted sources, removing associated user-owned source analyses and source-video feed associations per `TBD-019`. | User data control; `PG-004` | Deletion removes the source from the user's library and feed; unauthorized deletion attempts by another user are denied. |

---

## 6. Processing Status, Error Handling & Recovery

| ID | Classification | Requirement Statement | Rationale / Goal | Acceptance Criteria |
|---|---|---|---|---|
| **FR-024** | **CONFIRMED** | The system shall track and expose explicit source processing states (`Pending`, `Processing`, `Completed`, `PartiallyCompleted`, `Failed`) along with structured, user-safe failure reason codes. | Constraint #13; `DEC-P04`; `PG-001` | Client can query source status and display clear progress or failure feedback without exposing internal stack traces or secrets. |
| **FR-025** | **CONFIRMED** | When AI analysis or YouTube discovery encounters transient errors or timeouts, the system shall execute bounded retries and transition to a recoverable `Failed` or `PartiallyCompleted` state if retries are exhausted. | Constraint #13; `PG-005` | Transient failures retry up to configured bounds; exhausted failures persist a clean error state without leaving orphaned/corrupt partial records. |
| **FR-026** | **PROPOSED** | The system shall allow an authenticated user to retry processing for a `Failed` or `PartiallyCompleted` source without re-submitting the entire document (when source text is retained per `TBD-019`). | Recovery UX; Constraint #13 | Retry action re-queues the failed stage (AI analysis or video discovery) for the owning user's source. |

---

## 7. Conditional / Should-Have Functional Requirements (`TBD-018`)

> These requirements are **PROPOSED (Should Have)** and are active for Sprint 10 **only if confirmed** by the Product Owner (`TBD-018`, `TBD-008`).

| ID | Classification | Requirement Statement | Rationale / Goal | Acceptance Criteria |
|---|---|---|---|---|
| **FR-027** | **PROPOSED (Should Have)** | The system shall allow an authenticated user to bookmark and unbookmark discovered videos in their feed and view a saved bookmarks list (`TBD-018`). | `FEAT-012`; `WP-08` | Bookmark state persists per `(User, Video)` and is isolated to the owning user. |
| **FR-028** | **PROPOSED (Should Have)** | The system shall record user viewing history events for watched feed videos to support feed filtering/resume (`TBD-018`, `TBD-019`). | `FEAT-013`; `WP-08` | Viewing history is recorded per `(User, Video)` and deleted upon user account/history deletion request. |
| **FR-029** | **PROPOSED (Should Have)** | The system shall support self-service password reset for registered accounts (`TBD-008`). | `FEAT-015` | User can request a time-limited reset token via verified channel and set a new password meeting Identity policy. |
