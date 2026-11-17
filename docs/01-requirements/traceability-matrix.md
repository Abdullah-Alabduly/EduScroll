# EduScroll — Requirements Traceability Matrix

- **Document Version:** 0.1
- **Status:** Draft — Pending Product Owner Review & Approval
- **Work Package:** WP-03 (§19)
- **Last Updated:** 2026-11-17

---

## 1. End-to-End Traceability Matrix

| Goal | Feature | Requirements (FR / NFR / SEC / BR) | Workflow(s) | Primary Test Level(s) | Target Sprint |
|---|---|---|---|---|---|
| `PG-004` | `FEAT-001` | `FR-001`, `FR-002`, `FR-003`, `FR-004`, `SEC-001`, `SEC-002`, `SEC-004`, `SEC-005`, `SEC-011`, `NFR-001` | `WF-01` | Unit, API Integration, Security, Flutter Widget | S2, S3, S11 |
| `PG-001` | `FEAT-002` | `FR-005`, `FR-006`, `BR-003`, `SEC-007`, `SEC-013`, `NFR-010` | `WF-02`, `WF-03`, `WF-06` | Flutter Widget, Device Integration, API | S3, S4, S10 |
| `PG-001`, `PG-004` | `FEAT-003` | `FR-007`, `FR-008`, `BR-004`, `SEC-006`, `SEC-007`, `NFR-003` | `WF-04`, `WF-05`, `WF-06` | Unit (SSRF), Integration (HTTP mock), Security | S4, S11 |
| `PG-001`, `PG-004` | `FEAT-004` | `FR-009`, `FR-010`, `SEC-003`, `SEC-012`, `SEC-014`, `BR-006` | `WF-06`, `WF-14` | Unit, DB Integration, Security (IDOR) | S2, S4, S8 |
| `PG-001`, `PG-005` | `FEAT-005` | `FR-011`, `FR-012`, `FR-013`, `BR-005`, `NFR-005`, `NFR-006`, `NFR-007`, `SEC-009`, `SEC-010`, `SEC-015` | `WF-07` | Unit (Schema/Prompt Guard), Integration (Ollama), AI Eval, Privacy Review | S5, S8, S11 |
| `PG-002` | `FEAT-006` | `FR-014`, `FR-015`, `BR-005`, `NFR-007` | `WF-08` | Unit, AI Eval | S5, S6 |
| `PG-002` | `FEAT-007` | `FR-016`, `FR-017`, `FR-019`, `BR-001`, `SEC-008`, `NFR-003`, `NFR-009` | `WF-09`, `WF-11` | Unit (Dedup), Integration (YouTube API), Security (Secrets) | S6, S8 |
| `PG-002` | `FEAT-008` | `FR-018`, `NFR-007`, `NFR-009` | `WF-10`, `WF-11` | Unit (Scoring), Ranking Quality Eval | S7, S8 |
| `PG-003` | `FEAT-009` | `FR-020`, `FR-021`, `BR-002`, `SEC-003`, `NFR-001`, `NFR-010` | `WF-12`, `WF-13` | API Integration, Flutter Widget, E2E, Compliance Review | S9, S10, S11 |
| `PG-003`, `PG-004` | `FEAT-010` | `FR-022`, `FR-023`, `SEC-003`, `SEC-014`, `BR-006` | `WF-14` | API Integration, Security (IDOR), Flutter Widget | S4, S9, S10 |
| `PG-001`, `PG-002`, `PG-005` | `FEAT-011` | `FR-024`, `FR-025`, `FR-026`, `NFR-002`, `NFR-003`, `NFR-008` | `WF-15` | Unit, Fault-Injection Integration, E2E | S4, S8, S11 |
| `PG-003` (Should Have) | `FEAT-012` | `FR-027`, `SEC-003`, `BR-006` (`TBD-018`) | `WF-12`, `WF-13` | API Integration, Flutter Widget | S10 *(if approved)* |
| `PG-003` (Should Have) | `FEAT-013` | `FR-028`, `SEC-003`, `SEC-014` (`TBD-018`) | `WF-13` | API Integration, Privacy Review | S10 *(if approved)* |
| `PG-004` (Should Have) | `FEAT-015` | `FR-029`, `SEC-002`, `SEC-011` (`TBD-008`) | `WF-01` | API Integration, Security | S10 *(if approved)* |

---

## 2. Cross-Cutting Non-Functional & Architecture Traceability

| Requirement | Enforced By | Verified In |
|---|---|---|
| `NFR-004` (Availability/Backup) | Deployment & CI/CD infrastructure | S12 |
| `NFR-005` (Clean Architecture) | `EduScroll.Domain` / `Application` / `Infrastructure` / `API` references & `BR-007` | S1 (Arch Tests) – S11 |
| `NFR-008` (Structured Logging) | Cross-cutting logging & correlation middleware | S1, S8, S11 |
| `SEC-008` (No Secrets in Client/Git) | Secret scanning CI + env/secret injection (`BR-008`) | S1, S2, S3, S6, S11 |
