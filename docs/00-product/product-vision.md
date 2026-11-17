# EduScroll — Product Vision & Understanding

- **Document Version:** 0.1
- **Status:** Draft — Pending Product Owner Review & Approval
- **Work Package:** WP-01 (Product Understanding)
- **Last Updated:** 2026-11-17

---

## 1. Product Vision

> **CONFIRMED:** EduScroll is an AI-powered educational short-video discovery application that transforms user-provided educational materials (PDF documents and publicly accessible web pages) into a personalized vertical short-video discovery feed.

### Vision Statement (**PROPOSED**)
To bridge long-form study materials and short-form video learning by turning a learner's own readings into a focused, relevant stream of educational videos grounded in the concepts of their source material.

---

## 2. Problem Statement (**PROPOSED — Derived from Project Context**)

Learners frequently study from dense, text-heavy materials such as lecture PDFs, textbooks, research articles, and technical web pages. When they want concise visual explanations or alternative perspectives on unfamiliar concepts:
1. Searching general video platforms manually requires formulating multiple queries and sifting through off-topic, clickbait, or overly long results.
2. Mainstream short-video feeds are optimized for entertainment and engagement rather than alignment with a user's current study material.
3. Context is easily lost between reading a document/web page and discovering complementary video explanations.

---

## 3. Proposed Solution (**CONFIRMED Core Concept / PROPOSED Experience Framing**)

EduScroll automates concept extraction and educational video discovery from a user's study source:
1. **Source Ingestion:** The user submits a PDF document (text extracted locally on the client device per Constraint #3) or a publicly accessible web URL (content acquired securely by the backend per Constraints #5–#6).
2. **AI Concept Analysis:** An abstraction-backed AI pipeline extracts structured topics, subtopics, keywords, concepts, and a concise summary, validating all outputs against a strict schema before acceptance (Constraints #7–#8).
3. **Query Generation & Video Discovery:** The system generates targeted search queries and queries the YouTube Data API for candidate videos, storing only metadata and video references (Constraint #1).
4. **Relevance Ranking:** A ranking component evaluates candidate videos against the extracted educational topics and concepts.
5. **Vertical Short-Video Feed:** The user browses a vertical short-video feed, watches compliant embedded YouTube playback (Constraint #2), and can revisit the originating source context.

---

## 4. Core Value Proposition (**PROPOSED**)

- **Source-Grounded Discovery:** Videos are discovered from the actual topics and concepts of the learner's submitted material rather than generic popularity algorithms.
- **Reduced Search Friction:** Eliminates manual query crafting and filtering across long documents.
- **Focused Learning Experience:** Combines AI summarization/topic breakdown with a familiar vertical feed while keeping links back to the original study source.
- **Privacy & Policy Respect:** Processes PDF text locally on the client before sending text to the backend, and plays YouTube videos strictly via compliant metadata/embed references without hosting video files.

---

## 5. Product Goals

| Goal ID | Goal Description | Classification | Success Indicator (Measurable Thresholds Marked TBD) |
|---|---|---|---|
| **PG-001** | Enable authenticated users to transform a PDF or public web URL into a structured educational summary and topic breakdown. | **CONFIRMED** | End-to-end ingestion and validated AI structured output completes for supported inputs; limits per `TBD-005`, `TBD-006`, `TBD-007`. |
| **PG-002** | Automatically discover and rank relevant YouTube educational videos from analyzed source concepts without storing video media files. | **CONFIRMED** | Candidate videos fetched via YouTube Data API, ranked, and persisted as metadata-only references; relevance metrics per `WP-07` / `WP-10`. |
| **PG-003** | Deliver a vertical short-video feed in Flutter with policy-compliant playback and source traceability. | **CONFIRMED** | Users can scroll feed items, play embeddable videos, and view/revisit source summary/reference (`TBD-016`, `TBD-024`). |
| **PG-004** | Isolate each user's sources, analyses, and feeds securely while protecting backend infrastructure from SSRF, prompt injection, and secret leakage. | **CONFIRMED** | Ownership authorization enforced on all user-scoped resources; SSRF and schema validation verified in security testing. |
| **PG-005** | Maintain a modular, provider-agnostic, testable architecture suitable for iterative delivery from MVP to production. | **CONFIRMED** | Clean Architecture dependency rules enforced; AI provider swappable without rewriting Application/Domain logic. |

---

## 6. Explicit Non-Goals

The following are explicitly **NOT** goals of EduScroll (confirmed by Constraints or proposed to protect MVP focus per Constraint #15):

1. **No Video Hosting or Downloading (**CONFIRMED** — Constraint #1):** EduScroll will never download, transcode, cache media bytes, or re-host YouTube videos.
2. **No Bypassing YouTube Policies (**CONFIRMED** — Constraint #2):** No ad-stripping, raw stream extraction, or playback of non-embeddable videos via unofficial workarounds.
3. **No Server-Side PDF Binary Storage in MVP (**CONFIRMED** — Constraints #3 & #4):** PDF text extraction occurs locally in Flutter; raw PDF binary upload/storage is excluded from the initial MVP workflow.
4. **No Proprietary Video Creation / AI Video Generation (**PROPOSED**):** EduScroll discovers existing educational videos; it does not synthesize new video or audio.
5. **No Social Network or Multi-User Content Sharing in MVP (**PROPOSED** — `ASM-004`):** No public profiles, comments, follower graphs, or collaborative playlists in MVP.
6. **No Monetization / Subscriptions / Payments in MVP (**PROPOSED** — `ASM-001`, Section 4.1):** No billing, tiers, or payment gateways are part of Sprint 0–12 MVP scope unless explicitly added by the Product Owner (`TBD` if desired post-MVP).
7. **No Enterprise LMS / Multi-Tenancy Complexity (**CONFIRMED** — Constraint #15):** No SCORM/LTI integrations, institutional admin dashboards, or enterprise SSO in MVP.

---

## 7. Target User Groups

See [stakeholders.md](file:///C:/Users/Obad/Desktop/EduScroll/docs/00-product/stakeholders.md) for detailed stakeholder and user-class definitions. At a high level (**PROPOSED — Pending Confirmation**):
- **Primary Users (`Learner`):** University/high-school students, self-directed learners, and professionals studying technical or academic PDFs and web articles.
- **System Operators / Maintainers:** Engineering and operations personnel managing API keys, AI provider infrastructure, quotas, and deployments.
