# ADR-003: AI Provider Deployment Strategy (Local Ollama vs. Hosted LLM)

- **Status:** PROPOSED — Pending Product Owner Approval (`TBD-012`, `TBD-020`)
- **Date:** 2026-11-17

## 1. Decision Description
Ollama is confirmed as the initial local AI provider behind an AI provider abstraction (Constraint #7). However, running Ollama in production has major hardware, concurrency, and latency implications compared to a hosted LLM API.

## 2. Why It Matters
Standard CPU-only cloud app servers cannot run capable LLMs at acceptable latency or multi-user concurrency. Choosing production hosting without deciding GPU vs. managed API risks severe timeouts in Sprint 8–12.

## 3. Alternatives Considered
1. **Option A — Ollama for local dev/testing + Managed Cloud LLM API (via `IAiAnalysisProvider`) for production (or hybrid config):** Keeps zero per-token dev cost while ensuring production concurrency and structured-JSON reliability.
2. **Option B — Self-hosted Ollama on dedicated GPU infrastructure in production:** Keeps all user source text on operator-controlled infrastructure (stronger data residency), at higher fixed GPU hosting and ops cost.
3. **Option C — CPU-only Ollama in production:** Unacceptable latency and concurrency bottlenecks for multi-user workloads.

## 4. Trade-Offs
- **Option A:** Low ops overhead and elastic scaling in production; introduces per-token API cost and requires privacy disclosure (`SEC-015`, `TBD-020`) because extracted text goes to an external LLM vendor.
- **Option B:** Full data control and predictable fixed cost per GPU instance; requires GPU provisioning, model warm-up, scaling, and ops maintenance (`TBD-011`).

## 5. Recommendation
**PROPOSED:** Use **Ollama in local development and Sprint 5–8 integration**, implement `IAiAnalysisProvider` cleanly (Constraint #7), and decide between **Option A (Managed LLM API)** vs. **Option B (Dedicated GPU Ollama)** prior to Sprint 11/12 based on budget (`TBD-011`) and privacy constraints (`TBD-020`).

## 6. Approval Status
**Pending Product Owner Approval (`TBD-012`, `TBD-020`).**
