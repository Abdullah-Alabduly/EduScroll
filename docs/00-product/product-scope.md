# EduScroll — MVP Scope Definition (MoSCoW)

- **Document Version:** 0.1
- **Status:** Draft — Pending Product Owner Review & Approval
- **Work Package:** WP-02 (MVP Scope Definition)
- **Last Updated:** 2026-11-17

---

## 1. Scope Governance Principles

1. **Focused MVP (**CONFIRMED** — Constraint #15):** Only features directly required to fulfill the 11-step Intended User Journey (Brief §1.3) and Non-Negotiable Constraints (Brief §3) are classified as **Must Have**.
2. **No Unapproved Scope Expansion:** Any Should Have / Could Have item requires explicit Product Owner approval before entering a sprint backlog.
3. **Traceability:** Every feature links to one or more Product Goals (`PG-001`–`PG-005`) and Requirements (`FR-*`, `SEC-*`).

---

## 2. Feature Classification

### 2.1 Must Have (Required for MVP)

| Feature ID | Feature | Classification | Necessity Justification | Product Goal |
|---|---|---|---|---|
| **FEAT-001** | User Registration, Sign-In, Sign-Out & Token Refresh | **CONFIRMED** | Step 1 of the intended user journey; required to isolate user data and enforce ownership authorization (Constraint #12). | `PG-004` |
| **FEAT-002** | Local PDF Selection & On-Device Text Extraction (Flutter) | **CONFIRMED** | Step 2–3 of user journey and mandated by Constraints #3 & #4 (text extracted locally in Flutter and sent to backend). | `PG-001` |
| **FEAT-003** | Public Web URL Submission & Secure Backend Content Acquisition | **CONFIRMED** | Step 2–3 of user journey and mandated by Constraints #5 & #6 (backend fetches public URL with strict SSRF protection). | `PG-001` |
| **FEAT-004** | Source Text Validation, Cleaning & Persistence | **CONFIRMED** | Step 4 & 9 of user journey; prevents empty, malformed, oversized, or unsafe inputs from reaching AI/storage and enables revisiting sources (Constraint #13). | `PG-001`, `PG-004` |
| **FEAT-005** | Provider-Abstracted AI Content Analysis & Schema Validation | **CONFIRMED** | Step 5 of user journey; produces topics, subtopics, keywords, concepts, and summary behind `IAiAnalysisProvider` with strict output validation (Constraints #7, #8). | `PG-001`, `PG-005` |
| **FEAT-006** | Search Query Generation from Analyzed Concepts | **CONFIRMED** | Step 6 of user journey; translates extracted educational concepts into structured YouTube search queries. | `PG-002` |
| **FEAT-007** | YouTube Data API Video Discovery & Metadata-Only Storage | **CONFIRMED** | Step 7 & 9 of user journey; discovers candidate videos and persists metadata/references only (Constraint #1, #11). | `PG-002` |
| **FEAT-008** | Candidate Video Relevance Filtering & Ranking | **CONFIRMED** | Step 8 of user journey; scores and orders candidate videos by relevance to source topics/concepts and embeddability/availability. | `PG-002` |
| **FEAT-009** | Vertical Educational Short-Video Feed & Policy-Compliant Playback | **CONFIRMED** | Step 10 & 11 of user journey; presents ranked videos in a vertical feed with YouTube policy-compliant embedded playback (Constraint #2). | `PG-003` |
| **FEAT-010** | Source Library & Revisit Source View | **CONFIRMED** | Step 11 of user journey ("revisit their sources"); allows users to list their submitted sources, view analysis summaries/topics, open the feed for a source, and delete their sources (`TBD-024`). | `PG-003`, `PG-004` |
| **FEAT-011** | Processing Status Tracking, Timeouts, Retries & Failure Reporting | **CONFIRMED** | Mandated by Constraint #13; ensures partial/failed AI or YouTube steps are surfaced transparently and recoverable without data corruption. | `PG-001`, `PG-002`, `PG-005` |

### 2.2 Should Have (Valuable, Not Required for Initial Release)

> **PROPOSED:** Confirm with Product Owner whether `FEAT-012` and `FEAT-013` are deferred to Sprint 10/post-MVP (`TBD-018`). Note that WP-08 lists `Bookmark` and `Viewing History` as candidate data concepts, and Sprint 10 in WP-11 is titled "Complete User Experience", so `FEAT-012`/`FEAT-013` are candidates for Sprint 10 **only if approved**.

| Feature ID | Feature | Classification | Rationale | TBD / Dependency |
|---|---|---|---|---|
| **FEAT-012** | Video Bookmarking / Saved Videos | **PROPOSED (Should Have)** | Enhances learner retention by letting users save high-value videos, but core discovery loop works without it. | `TBD-018` |
| **FEAT-013** | Viewing History & Watched State Tracking | **PROPOSED (Should Have)** | Prevents showing already-watched videos repeatedly and lets learners resume; not strictly required to validate core source-to-feed loop. | `TBD-018` |
| **FEAT-014** | Unified Cross-Source Personal Feed & Source Re-Discovery/Refresh | **PROPOSED (Should Have)** | Allows combining videos across multiple submitted sources or refreshing stale results (`TBD-017`). | `TBD-017`, `TBD-015` |
| **FEAT-015** | Password Reset / Email Verification Flow | **PROPOSED (Should Have / Conditional Must)** | Essential for public production launch (Sprint 12), though initial dev/beta milestones may depend on `TBD-008`. | `TBD-008` |

### 2.3 Could Have (Optional Future Enhancement)

| Feature ID | Feature | Classification | Notes |
|---|---|---|---|
| **FEAT-016** | On-Device or Backend OCR for Scanned PDFs | **PROPOSED (Could Have)** | Deferred unless `TBD-006` requires scanned PDF support in MVP. |
| **FEAT-017** | Topic-Level Feed Filtering / Study Flashcard Export | **PROPOSED (Could Have)** | Post-MVP enhancement leveraging stored topics/summaries. |
| **FEAT-018** | User Relevance Feedback (Thumbs Up/Down) for Ranking Tuning | **PROPOSED (Could Have)** | Valuable once baseline ranking dataset and evaluation exist (`WP-07`, `WP-10`). |
| **FEAT-019** | Offline Metadata & Source Summary Caching in Flutter | **PROPOSED (Could Have)** | Improves mobile experience when offline (video playback still requires network per YouTube ToS). |

### 2.4 Out of Scope (Explicitly Excluded from Current Release)

| Feature | Classification | Reason for Exclusion |
|---|---|---|
| Downloading, caching, or re-hosting YouTube video/audio streams | **CONFIRMED Out of Scope** | Violates Non-Negotiable Constraints #1 and #2. |
| Subscription plans, payments, ads, or monetization | **CONFIRMED Out of Scope** | Not part of project context; prohibited from invention (§4.1). |
| Multi-tenant organizations, teacher/admin portals, or social sharing | **CONFIRMED Out of Scope** | Violates MVP focus (Constraint #15) and has no approved roles (§4.1). |
| AI video generation / synthetic narration | **CONFIRMED Out of Scope** | Outside product concept (discovery of existing YouTube videos). |
| Arbitrary third-party video platforms (Vimeo, TikTok, etc.) | **PROPOSED Out of Scope** | Initial technical direction specifies YouTube Data API only. |
| Multi-file batch ingestion or full textbook chapter splitting automation | **PROPOSED Out of Scope** | Beyond focused single-source MVP submission (`TBD-013` covers single-document length handling). |
