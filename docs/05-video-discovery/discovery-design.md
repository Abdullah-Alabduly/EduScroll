# EduScroll — YouTube Video Discovery Design (WP-07)

- **Document Version:** 0.1
- **Status:** Draft — Pending Product Owner Review & Approval
- **Work Package:** WP-07 (Video Discovery Design)
- **Last Updated:** 2026-11-17

---

## 1. Search Query Generation (`WF-08`)
- **Input:** Validated `SourceAnalysis` (`topics`, `subtopics`, `keywords`, `concepts`).
- **Strategy (**PROPOSED**):**
  1. Generate a bounded set of focused queries (`TBD-014`, e.g., combining core topic + specific concept, avoiding overly broad single-word queries and overly restrictive long sentences).
  2. Normalize whitespace, remove unsupported/unsafe punctuation, deduplicate near-identical queries, and cap total queries per source (`TBD-014`) to protect YouTube API quota (`NFR-009`).

---

## 2. YouTube Data API Integration Boundaries (`Constraint #1, #2, #11`)
- **Location:** Encapsulated behind `IVideoDiscoveryService` (`Application` port) implemented in `EduScroll.Infrastructure`.
- **Credential Isolation (`SEC-008`, `BR-008`):** API key/credentials live exclusively in backend configuration/secrets—never exposed to Flutter.
- **Metadata-Only Rule (`BR-001`):** Only video identifiers (`YouTubeVideoId`) and metadata fields are retrieved and stored; media streams are never downloaded.

---

## 3. Candidate Video Metadata & Filtering
1. **Discovery Call (`search.list`):** Query by sanitized search string with type `video`, `videoEmbeddable=true` (when filtering for embeddable videos), optional `videoDuration` (`TBD-016`), and `relevanceLanguage` (`TBD-004`).
2. **Metadata Enrichment Call (`videos.list`):** Batch unique discovered `YouTubeVideoId`s (up to API batch size) to fetch authoritative metadata:
   - `snippet`: `title`, `description`, `channelId`, `channelTitle`, `publishedAt`, `thumbnails`, `tags` (optional), `defaultAudioLanguage`
   - `contentDetails`: `duration` (ISO 8601), `caption`
   - `status`: `embeddable`, `privacyStatus`, `uploadStatus`
   - `statistics`: `viewCount`, `likeCount` (if available; channels/videos may hide counts)
3. **Mandatory Policy & Eligibility Filters:**
   - Reject if `status.embeddable != true` (`BR-002`).
   - Reject if `status.privacyStatus != "public"` or video is unavailable.
   - Apply duration bounds (`TBD-016` — defining what qualifies as "short-video" educational content).

---

## 4. Duplicate Detection (`FR-017`)
- Multiple queries for the same source frequently return overlapping videos.
- **Within a Single Source Run:** Deduplicate candidate IDs in memory by `YouTubeVideoId` prior to calling `videos.list` (saving quota) and preserve provenance of which queries/topics matched the video for ranking (`WP-07`).
- **Across Sources (`TBD-023`):** `DiscoveredVideo` metadata can be keyed uniquely by `YouTubeVideoId` (or per-source depending on ToS caching TTL `TBD-015` and isolation policy `TBD-023`).

---

## 5. Missing Metadata, Availability & Quota Considerations

| Concern | Design Mitigation (**PROPOSED**) | Unresolved / Verification Item |
|---|---|---|
| **Missing Metadata** | Handle null/absent `tags`, `defaultAudioLanguage`, `likeCount`, or empty `description` gracefully in ranking/DTOs without throwing `NullReferenceException`. | Ranking fallback weights when `description`/`tags` are empty (`ranking-strategy.md`). |
| **Video Availability Drift** | Videos can become private/deleted/non-embeddable *after* discovery. Flutter player handles playback errors gracefully (`WF-13`); optional metadata refresh respects `TBD-015` TTL. | Cache TTL & refresh policy (`TBD-015`). |
| **YouTube API Quota Exhaustion** | Cap queries per source (`TBD-014`), deduplicate before `videos.list`, enforce per-user submission rate limits (`SEC-011`, `TBD-021`), and handle HTTP `403 quotaExceeded` gracefully (`WF-15`). | Verify exact current quota costs and ToS metadata storage/caching rules against official Google/YouTube Data API v3 documentation prior to Sprint 6 (`TBD-015`). |
