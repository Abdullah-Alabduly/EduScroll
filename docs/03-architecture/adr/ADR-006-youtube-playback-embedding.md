# ADR-006: Policy-Compliant YouTube Video Playback in Flutter

- **Status:** PROPOSED — Pending Product Owner Approval (`DEC-P06`)
- **Date:** 2026-11-17

## 1. Decision Description
Non-Negotiable Constraints #1 and #2 prohibit downloading/hosting YouTube videos and mandate compliance with YouTube API policies, branding requirements, and embedding restrictions.

## 2. Why It Matters
Using unofficial stream-extraction libraries (which scrape raw `.mp4`/DASH URLs for native `video_player`) violates YouTube Terms of Service, strips YouTube branding/ads, and ignores `embeddable` restrictions.

## 3. Alternatives Considered
1. **Option A — Official/compliant WebView-based YouTube IFrame Player API integration inside Flutter:** Plays directly from YouTube's official embed player, preserves YouTube branding, controls, view counting, and enforces uploader embedding restrictions.
2. **Option B — External deep-link launch into YouTube app/browser:** Guaranteed compliant, but breaks the in-app vertical short-video discovery experience.
3. **Option C — Raw stream URL extraction into native video player:** **REJECTED** — violates Constraint #2 and YouTube ToS.

## 4. Trade-Offs
- **Option A** delivers an in-app vertical feed while respecting Constraint #2. Operational caveat: WebView-based players require careful controller disposal/preloading (only 1 active WebView player at a time during vertical scroll) and a Sprint 3/9 device spike to verify Android/iOS embed behavior (some music/copyrighted videos still restrict embedding even when flagged, requiring graceful fallback UI per `WF-13`).

## 5. Recommendation
**PROPOSED:** Adopt **Option A** (YouTube IFrame Player API inside an embedded Flutter WebView wrapper), validated via Sprint 3/9 prototype spike (`TBD-022`), with "Open in YouTube" fallback if an uploader restricts mobile WebView embedding.

## 6. Approval Status
**Pending Product Owner Approval (`DEC-P06`).**
