# ADR-004: Source Processing Execution Model (Async Background vs. Synchronous)

- **Status:** PROPOSED — Pending Product Owner Approval (`DEC-P04`)
- **Date:** 2026-11-17

## 1. Decision Description
After a user submits extracted PDF text or a web URL, the backend must fetch/clean text, invoke AI inference, query YouTube, rank videos, and persist metadata.

## 2. Why It Matters
Combining web fetch, LLM inference, and multiple YouTube API calls in a single synchronous HTTP request risks mobile and reverse-proxy HTTP timeouts, poor UX, and lost work on transient disconnects.

## 3. Alternatives Considered
1. **Option A — Asynchronous in-process background worker (`BackgroundService` + PostgreSQL-backed durable state/polling) with `202 Accepted` + client status polling:** Simple, zero external broker dependencies, survives transient client disconnects.
2. **Option B — Synchronous HTTP request:** Simple code initially, but prone to timeouts under Ollama/LLM latency and blocked mobile UI.
3. **Option C — Distributed message broker (RabbitMQ/Kafka/MassTransit) + SignalR push:** Over-engineered for MVP (Constraint #15).

## 4. Trade-Offs
- **Option A** supports Constraint #13 (timeouts, retries, incomplete processing recovery), Constraint #15 (focused MVP without heavy broker infra), and clean state transitions (`Pending` -> `Completed`/`Failed`). Polling adds modest client request overhead during processing.
- **Option A scaling note:** If multiple backend replicas run in Sprint 12, PostgreSQL row-locking/lease claiming (`FOR UPDATE SKIP LOCKED` or status-claim pattern) prevents duplicate job execution without adding Redis/RabbitMQ.

## 5. Recommendation
**PROPOSED:** Adopt **Option A** (lightweight PostgreSQL-coordinated background worker + `202 Accepted` + status polling).

## 6. Approval Status
**Pending Product Owner Approval (`DEC-P04`).**
