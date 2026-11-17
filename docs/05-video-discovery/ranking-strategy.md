# EduScroll — Video Relevance & Ranking Strategy (WP-07)

- **Document Version:** 0.1
- **Status:** Draft — EXPERIMENTAL Proposal Pending Dataset Evaluation & Product Owner Approval
- **Work Package:** WP-07 (Video Ranking Strategy)
- **Last Updated:** 2026-11-17

> **Strict Anti-Hallucination & Evidence Notice:** No ranking formula or weight set is claimed effective here. Any preliminary weights below are **EXPERIMENTAL** starting hypotheses that **must** be evaluated against a labeled test dataset (`testing-strategy.md`) before production reliance.

---

## 1. Filtering Stage (Pre-Ranking Eligibility)
Before scoring, candidates must pass hard eligibility gates:
1. **Policy & Availability (`BR-002`):** `status.embeddable == true` and `privacyStatus == "public"`.
2. **Duration Filter (`TBD-016`):** Within approved short-video educational duration bounds (e.g., excluding multi-hour livestreams unless `TBD-016` allows longer clips).
3. **Language / Safety Filter (`TBD-004`, `TBD-016`):** Optional match on language/metadata when available.

---

## 2. Preliminary Multi-Signal Relevance Model (**PROPOSED / EXPERIMENTAL**)

Each eligible candidate video $v$ is evaluated against `SourceAnalysis` $A$ (`topics`, `subtopics`, `concepts`, `keywords`, `summary`) using transparent, deterministic, unit-testable signals in `EduScroll.Application`:

| Signal | What It Captures | Data Source | Notes / Edge Cases |
|---|---|---|---|
| **Topic Match ($S_{\text{topic}}$)** | Overlap between video title/description/tags and primary `topics` / `subtopics` of $A$. | `A.Topics`, `v.Title`, `v.Description` | Strongest indicator of subject alignment. |
| **Concept Match ($S_{\text{concept}}$)** | Presence of specific `concepts` / `keywords` from $A$ in `v.Title` and `v.Description` (normalized/tokenized). | `A.Concepts`, `A.Keywords`, `v` text | Helps distinguish specific explanations from generic topic intros. |
| **Title Match ($S_{\text{title}}$)** | Direct lexical/phrase coverage of generated `SearchQuery` or core terms in `v.Title`. | `v.Title` | High precision signal when descriptions are sparse. |
| **Description Match ($S_{\text{desc}}$)** | Term density/coverage in `v.Description` (with length normalization so spammy keyword-stuffed descriptions do not dominate). | `v.Description` | Must handle empty descriptions gracefully (`0` contribution or neutral fallback). |
| **Query Rank / Multi-Query Hit ($S_{\text{query}}$)** | Whether video appeared across multiple distinct source queries and its YouTube search rank position. | Discovery metadata | Rewards cross-query consensus. |
| **Justified Quality / Educational Proxies ($S_{\text{meta}}$)** | Bounded, non-dominant tie-breakers: presence of captions (`caption == true`), duration fit (`TBD-016`), and log-scaled engagement/recency (`viewCount` only as weak tie-breaker so niche accurate tutorials are not buried). | `videos.list` metadata | Avoid over-weighting viral popularity over topical relevance. |

### Experimental Composite Score (Illustrative Placeholder Only)
$$\text{Score}(v, A) = w_1 S_{\text{topic}} + w_2 S_{\text{concept}} + w_3 S_{\text{title}} + w_4 S_{\text{desc}} + w_5 S_{\text{query}} + w_6 S_{\text{meta}}$$

- **Status of Weights ($w_1 \dots w_6$):** **EXPERIMENTAL — Uncalibrated (`TBD`).** Must not be hardcoded as magic numbers; configure via options and calibrate in Sprint 7 against a human-labeled relevance benchmark (`WP-10`).
- **Minimum Relevance Cutoff (`TBD`):** Candidates below a calibrated minimum threshold are excluded (or trigger `PartiallyCompleted` with `NO_RELEVANT_VIDEOS` if zero remain).

---

## 3. Optional AI Re-Ranking (Post-Baseline Evaluation Only)
- If deterministic lexical matching struggles with synonyms or semantic mismatches in Sprint 7 evaluation, an optional `IAiAnalysisProvider` re-ranking step on top-$K$ candidates may be evaluated—strictly weighing latency (`TBD-010`), cost, and prompt-injection risks (`SEC-009` on untrusted YouTube titles/descriptions).

---

## 4. Evaluation Requirement
Before Sprint 7 exit:
1. Build a curated benchmark of `(SourceAnalysis, CandidateVideos)` with human-annotated relevance labels (e.g., Irrelevant / Somewhat Relevant / Highly Relevant).
2. Measure retrieval/ranking metrics (e.g., Precision@K, NDCG@K) to validate whether the model outperforms raw YouTube order.
