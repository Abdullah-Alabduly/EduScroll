# EduScroll — AI Output Evaluation Strategy (WP-06)

- **Document Version:** 0.1
- **Status:** Draft — Pending Product Owner Review & Approval
- **Work Package:** WP-06 & WP-10 (AI Evaluation)
- **Last Updated:** 2026-11-17

> **Rule Compliance:** Do not invent model accuracy percentages or unverified benchmarks. All pass thresholds are marked `TBD` until a golden evaluation dataset is curated in Sprint 5.

---

## 1. Objectives
1. Verify objectively that candidate models (starting with local Ollama candidates for `TBD-012`) reliably produce valid structured output matching `FR-012` / `SEC-010`.
2. Measure whether extracted `topics`, `subtopics`, `keywords`, `concepts`, and `summary` accurately represent source documents without hallucinated concepts.
3. Prevent regressions when changing prompt templates, context-window handling (`TBD-013`), or swapping AI providers (`Constraint #7`).

---

## 2. Evaluation Dataset (**PROPOSED — To Build in Sprint 5**)
Assemble a fixed, versioned golden test suite of representative educational texts (stored in test fixtures, free of private user data), covering:
- Short and long academic lecture excerpts (`PDF`-extracted text patterns, including headings/bullet noise).
- Technical web articles (`WebUrl`-extracted text patterns).
- Edge cases: minimal text, noisy boilerplate, non-educational spam, and adversarial prompt-injection payloads (`SEC-009`).
- Approved target languages (`TBD-004`).

---

## 3. Evaluation Dimensions & Metrics

| Dimension | What Is Measured | Target Threshold |
|---|---|---|
| **Schema Compliance Rate** | % of runs producing parseable JSON passing all `OutputValidator` constraints (`FR-013`, `SEC-010`) on 1st attempt vs. after retry. | `TBD` (goal: near-deterministic before prod) |
| **Injection Resistance** | % of adversarial prompt-injection fixtures where output preserves schema and ignores embedded malicious instructions (`SEC-009`). | 100% schema/safety enforcement (validator gate) |
| **Topic/Concept Grounding** | Human/rubric check (or LLM-as-judge cross-check, optional) verifying extracted concepts actually appear in or are directly supported by source text (low hallucination). | `TBD` — Requires PO/Team Rubric |
| **Query Usability** | % of generated `SearchQuery` strings that return non-empty, on-topic YouTube candidates in `WF-09`. | `TBD` |
| **End-to-End Inference Latency** | Measured p50/p95 response time per document length bucket on target hardware (`TBD-010`, `TBD-012`). | `TBD-010` |

---

## 4. Operational Process
- Run the evaluation harness offline whenever changing `PromptVersion`, switching Ollama models (`TBD-012`), or adding a new `IAiAnalysisProvider` adapter.
- Record model name, quantization/parameters, prompt version, and observed metrics in Sprint 5/8 evaluation logs before approving a model for production.
