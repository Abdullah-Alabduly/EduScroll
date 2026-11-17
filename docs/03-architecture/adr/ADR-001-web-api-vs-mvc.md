# ADR-001: Resolve `EduScroll.Web` MVC Scaffold vs. ASP.NET Core 10 Web API

- **Status:** APPROVED — Implemented in Sprint 1 (`EduScroll.API` under `backend/`)
- **Date:** 2026-11-17

## 1. Decision Description
The existing repository (`src/EduScroll.Web`) is scaffolded as an ASP.NET Core MVC web application with Razor Views (`Controllers/HomeController.cs`, `Views/`, `wwwroot/lib/bootstrap`, `jquery`), whereas the Sprint 0 brief specifies `ASP.NET Core 10 Web API` communicating via `REST API` with a `Flutter` client.

## 2. Why It Matters
Leaving MVC Razor views, cookie-oriented `ErrorViewModel` HTML handlers, and client-side Bootstrap/jQuery assets in the API host creates unnecessary attack surface, confuses JSON vs. HTML error handling, and misaligns project naming and dependencies for Sprint 1.

## 3. Alternatives Considered
1. **Option A — Convert `EduScroll.Web` (or rename/replace with `EduScroll.Api`) into a pure ASP.NET Core 10 Web API:** Remove `Views/`, `wwwroot/lib/`, `AddControllersWithViews()`, and `HomeController`; configure `AddControllers()`, JWT Bearer auth, `ProblemDetails`, and OpenAPI.
2. **Option B — Keep `EduScroll.Web` as MVC and add a separate `EduScroll.Api` project:** Increases solution complexity when no Razor web UI is in MVP scope.
3. **Option C — Host REST API controllers inside the existing MVC project alongside Razor views:** Retains dead MVC/jQuery dependencies and increases maintenance/security surface.

## 4. Trade-Offs
- **Option A** keeps a single, clean API composition root matching Constraint #15 (avoid unnecessary complexity) and Clean Architecture. Requires removing unused template files in Sprint 1.
- **Option B/C** preserve unused Razor/jQuery assets without an approved web frontend requirement.

## 5. Recommendation
**PROPOSED:** Adopt **Option A** (convert/replace `EduScroll.Web` into a pure ASP.NET Core 10 Web API project, resolving `CONF-003` configuration placement at the same time).

## 6. Approval Status
**Pending Product Owner Approval (`TBD-001`).**
