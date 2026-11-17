# EduScroll — Stakeholders & User Classes

- **Document Version:** 0.1
- **Status:** Draft — Pending Product Owner Review & Approval
- **Work Package:** WP-01 & WP-03 (§4–5)
- **Last Updated:** 2026-11-17

---

## 1. Stakeholders

| Stakeholder | Role / Interest | Key Concerns | Classification |
|---|---|---|---|
| **Product Owner** | Defines product vision, approves requirements, scope, TBDs, and sprint priorities. | Product-market fit, MVP delivery focus, policy compliance, cost vs. quality trade-offs. | **CONFIRMED** |
| **End Users (Learners)** | Submit PDFs/URLs and consume educational short-video feeds. | Relevant video discovery, clear source summaries, reliable playback, data privacy, responsive UI. | **CONFIRMED** |
| **Engineering Team (Backend, Mobile, AI/ML)** | Designs, builds, tests, and maintains the ASP.NET Core 10 API, Flutter app, and AI/ranking pipelines. | Clean Architecture boundaries, testability, provider abstraction, schema validation, manageable complexity. | **CONFIRMED** |
| **Operations / DevOps Maintainer** | Deploys, monitors, secures secrets, and manages CI/CD, PostgreSQL, Ollama/LLM hosts, and API quotas. | Secret isolation, YouTube API quota exhaustion, LLM latency/GPU cost, SSRF and abuse prevention. | **PROPOSED** |
| **External Platform Providers (Google/YouTube, LLM Providers)** | Provide YouTube Data API v3 / IFrame Player and hosted LLM inference (if used). | Adherence to YouTube API Services Terms of Service, branding, embeddability, and quota limits. | **CONFIRMED (YouTube) / PROPOSED (Hosted LLM)** |

---

## 2. User Classes

> **Strict Anti-Hallucination Note (§4.1):** No specialized user roles (e.g., Admin, Moderator, Instructor, Premium Subscriber) are defined in the project context. Only a single authenticated end-user class is assumed for MVP (`ASM-001`, `DEC-P09`). Any additional roles require explicit Product Owner decision (`TBD`).

| User Class | Description | Access & Capabilities | Authentication Requirement | Classification |
|---|---|---|---|---|
| **Unauthenticated Visitor** | User who has opened the Flutter app without an active valid session. | May access Registration, Sign-In, and Password Reset (if enabled per `TBD-008`) screens only. Cannot submit sources or view feeds. | None | **CONFIRMED** (from Journey Step 1) |
| **Authenticated Learner (`Learner`)** | Registered and signed-in individual user. | May submit PDFs/URLs, monitor processing status, view/delete own sources, browse own ranked short-video feeds, play embedded YouTube videos, and revisit own source summaries/references. | Valid JWT Bearer token (`SEC-001`–`SEC-005`) | **ASSUMPTION — Requires Confirmation (`ASM-001`, `DEC-P09`)** |
| **System Administrator / Operator** | *Not a runtime in-app user role in MVP.* Infrastructure/ops tasks (secrets, logs, quota monitoring, deployments) happen via ops/hosting tooling rather than an in-app admin UI. | Out of scope as an in-app role for MVP unless requested (`TBD`). | Infrastructure/CI access only | **PROPOSED (`DEC-P09`)** |

---

## 3. Target User Segments (**PROPOSED — Requires Confirmation**)

1. **Higher Education & Self-Taught STEM/Academic Students:** Upload lecture slides or textbook chapter PDFs to quickly find concise visual explanations of difficult concepts.
2. **Online Learners & Professionals:** Paste technical blog posts, documentation pages, or articles (URLs) to supplement reading with curated short educational clips.
