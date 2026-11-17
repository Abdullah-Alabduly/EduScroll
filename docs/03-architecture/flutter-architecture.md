# EduScroll — Flutter Client Architecture

- **Document Version:** 0.1
- **Status:** Draft — Pending Product Owner Review & Approval
- **Work Package:** WP-05 (Flutter Conceptual Architecture)
- **Last Updated:** 2026-11-17

> **Note:** No Flutter project exists in the repository yet. No specific third-party Dart/Flutter packages are assumed selected; package selection is deferred to Sprint 3 spikes (`TBD-022`, `ADR-005`, `ADR-006`).

---

## 1. Conceptual Feature-First Layered Architecture (**PROPOSED**)

To keep the MVP modular and testable without over-engineering (Constraint #15), a **feature-first structure with clean internal separation** is proposed:

```text
lib/
├── core/         # Config, HTTP client wrapper, auth interceptor, secure storage, error mapping, theme
├── features/
│   ├── auth/     # Sign-in, registration, session state
│   ├── sources/  # PDF picker, local PDF extractor, URL submission, processing status, source library/detail
│   └── feed/     # Vertical short-video feed, compliant YouTube player wrapper, (conditional bookmarks)
└── app.dart      # Router & root composition
```

Within each feature: `presentation/` (widgets, screens, view state) -> `application/` (state notifiers/controllers) -> `data/` (API data sources, DTO mappers, local platform services).

---

## 2. Architectural Areas & Responsibilities

| Area | Responsibility & Design Guidelines | Status / Unresolved Decision |
|---|---|---|
| **Presentation** | Declarative UI widgets, vertical `PageView` feed, PDF/URL submission forms, processing progress states, source summary/revisit views. Must contain zero HTTP, parsing, or token logic. | **PROPOSED** |
| **State Management** | Manages async loading/data/error states across Auth, Source Processing Polling, and Paginated Feed. **No package is assumed selected.** **PROPOSED (`ADR-005`):** Recommend **Riverpod** for compile-safe DI, explicit `AsyncValue`, and easy unit/widget mocking; **BLoC/Cubit** as structured alternative. | **PROPOSED — Pending Approval (`ADR-005`, `TBD-022`)** |
| **Navigation** | Auth-guarded declarative routing (redirecting unauthenticated users to Sign-In; deep-linking between Feed item -> Source Detail -> Source Feed). | **PROPOSED** (Router package evaluated in Sprint 3, `TBD-022`) |
| **Data Access & API Communication** | Typed repository/data-source classes talking to ASP.NET Core REST API over HTTPS. Includes auth header injection, `401` single-flight token refresh interceptor, `ProblemDetails` parsing, and retry/timeout handling. | **PROPOSED** (HTTP client package evaluated in Sprint 3, `TBD-022`) |
| **Authentication State** | Reactive `AuthState` (`Unknown`, `Authenticated`, `Unauthenticated`). Hydrates on app startup from secure storage; clears tokens and cached user state immediately on sign-out or unrecoverable `401`. | **PROPOSED (`DEC-P10`)** |
| **Error Handling** | Maps `ProblemDetails` HTTP responses, network timeouts, and local PDF extraction failures into user-actionable localized UI messages (no raw stack traces or server internals). | **CONFIRMED (Constraint #13)** |
| **Local PDF Processing** | Implements `WF-03` on-device: file selection -> file size check (`TBD-005`) -> off-UI-thread (`Isolate`/background channel) text extraction -> empty/scanned PDF check (`TBD-006`) -> sends **text only** to backend (`BR-003`, `SEC-013`). | **CONFIRMED Boundary / Package `TBD-022`** |
| **Secure Storage** | Stores JWT access token and refresh token strictly in OS-encrypted storage (Android Keystore / iOS Keychain wrapper). **Never** stores YouTube API keys, AI keys, or passwords (`BR-008`, `SEC-005`, `SEC-008`). | **CONFIRMED (`SEC-005`, `SEC-008`)** |
| **YouTube Playback** | Embeds policy-compliant YouTube player (`ADR-006`, `BR-002`) showing YouTube branding/controls, respecting `embeddable` restrictions, and managing single active video controller lifecycle during vertical scrolling to prevent memory/audio leaks. | **CONFIRMED Constraint #2 / Package `ADR-006`** |

---

## 3. Platform & Lifecycle Considerations
- **Target Platforms (`TBD-003`):** `ASM-002` assumes Android and iOS mobile first. Flutter Web/Desktop introduces CORS, file-handle, and WebView/IFrame differences and should be explicitly scoped in `TBD-003`.
- **Background Polling Lifecycle:** When the user backgrounds the app during source processing (`Pending`/`Analyzing`), polling pauses gracefully and resumes/refreshes status when returning to foreground.
