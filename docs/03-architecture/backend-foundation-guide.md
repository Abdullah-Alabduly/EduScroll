# EduScroll — Backend Foundation, Setup & Operations Guide

- **Document Version:** 1.1
- **Status:** Implemented (Sprint 1 — Docker Runtime Verification Pending External Host)
- **Last Updated:** 2026-11-17

---

## 1. Repository & Backend Project Structure

```text
EduScroll/
├── .gitignore
├── docs/
└── backend/
    ├── EduScroll.sln
    ├── Directory.Build.props
    ├── Dockerfile
    ├── .dockerignore
    ├── src/
    │   ├── EduScroll.Domain/         # DomainException base contract (no external refs)
    │   ├── EduScroll.Application/    # AddApplication(), transport-agnostic AppException hierarchy
    │   ├── EduScroll.Infrastructure/ # AddInfrastructure(), TimeProvider.System registration
    │   └── EduScroll.API/            # Composition root, Options validation, ProblemDetails,
    │                                 # CorrelationIdMiddleware, CORS, OpenAPI, HealthController
    └── tests/
        ├── EduScroll.Domain.Tests/
        ├── EduScroll.Application.Tests/
        ├── EduScroll.Architecture.Tests/
        └── EduScroll.API.IntegrationTests/
```

---

## 2. Local Development Setup

### Prerequisites
- **.NET 10 SDK** (`10.0.301` / `10.0.401` or newer `10.0.x` patch).
- **Docker** *(not installed on initial Sprint 1 workstation; required to build/run container images)*.

### Build & Run Locally
```bash
dotnet restore backend/EduScroll.sln
dotnet build backend/EduScroll.sln
dotnet run --project backend/src/EduScroll.API/EduScroll.API.csproj
```
Default local URLs from [launchSettings.json](file:///C:/Users/Obad/Desktop/EduScroll/backend/src/EduScroll.API/Properties/launchSettings.json):
- HTTP: `http://localhost:5221`
- HTTPS: `https://localhost:7017`
- OpenAPI JSON *(Development only)*: `/openapi/v1.json`
- Swagger UI *(Development only)*: `/swagger`

---

## 3. Configuration Reference

Environment-specific configuration files in `backend/src/EduScroll.API/`:
- [appsettings.json](file:///C:/Users/Obad/Desktop/EduScroll/backend/src/EduScroll.API/appsettings.json): Production-safe defaults (`EnableOpenApi: false`, empty `Cors:AllowedOrigins`, `AllowedHosts: "localhost;127.0.0.1"`, `HttpsRedirection:Enabled: true`).
- [appsettings.Development.json](file:///C:/Users/Obad/Desktop/EduScroll/backend/src/EduScroll.API/appsettings.Development.json): Enables OpenAPI/Swagger UI and explicit localhost dev CORS origins.
- [appsettings.Testing.json](file:///C:/Users/Obad/Desktop/EduScroll/backend/src/EduScroll.API/appsettings.Testing.json): Disables OpenAPI and HTTPS redirection for integration testing with explicit test CORS origin.
- [appsettings.Example.json](file:///C:/Users/Obad/Desktop/EduScroll/backend/src/EduScroll.API/appsettings.Example.json): Safe placeholder template (no secrets).

Non-production config files (`appsettings.Development.json`, `appsettings.Testing.json`, `appsettings.Example.json`) are explicitly excluded from publish outputs in [EduScroll.API.csproj](file:///C:/Users/Obad/Desktop/EduScroll/backend/src/EduScroll.API/EduScroll.API.csproj) (`CopyToPublishDirectory="Never"`) and excluded from Docker build context in [.dockerignore](file:///C:/Users/Obad/Desktop/EduScroll/backend/.dockerignore).

### Environment Variables

| Variable | Purpose | Production Default / Rule |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | Runtime environment (`Development`, `Testing`, `Production`) | `Production` (set in `Dockerfile`) |
| `ASPNETCORE_URLS` | Kestrel binding URLs | `http://+:8080` in container |
| `AllowedHosts` | Semicolon-delimited host allowlist for `HostFilteringMiddleware` | `"localhost;127.0.0.1"` in `appsettings.json`; override with production domain(s) (e.g., `api.eduscroll.example;localhost`) |
| `Api__Title`, `Api__Version`, `Api__Description` | OpenAPI metadata (`ApiMetadataOptions`, validated at startup) | `EduScroll API`, `v1` |
| `Api__EnableOpenApi` | Controls OpenAPI/Swagger mapping (additionally gated by `IsDevelopment()`) | `false` (never exposed in Production) |
| `Cors__AllowedOrigins__0` *(etc.)* | Explicit allowed CORS origins (`CorsOptionsValidator` rejects `*` and non-HTTPS in Production) | `[]` (cross-origin denied by default) |
| `Cors__AllowCredentials` | Allow credentials on explicit origins (rejected if `AllowedOrigins` is empty) | `false` |
| `HttpsRedirection__Enabled` | Toggles HTTP-to-HTTPS redirect middleware | `true` in `appsettings.json`; `false` in `Dockerfile` (TLS terminated at reverse proxy/ingress) |

---

## 4. Health Endpoints & Sprint 2 Extension Plan

| Endpoint | Purpose (Sprint 1) | Sprint 2+ Extension Point |
|---|---|---|
| `GET /health/live` | Runs `SelfLivenessCheck` (`live` tag); returns `200 {"status":"Healthy"}` if process responds. | Remains lightweight process liveness probe (no external dependency checks). |
| `GET /health/ready` | Runs `FoundationReadinessCheck` (`ready` tag) verifying core Sprint 1 options/services only. **Does not fake DB readiness.** | Sprint 2 will register PostgreSQL / EF Core connection readiness check (`ready` tag) and future downstream dependency checks. |

Both endpoints exclude machine names, environment variables, configuration values, and diagnostic timings.

---

## 5. Observability & Error Handling Extension Points

- **Error Mapping:** `GlobalExceptionHandler` maps `ValidationAppException` (`400`), `DomainException` (`422`), `AppException` (`400`/`403`/`404`/`409`/`502`), and unexpected exceptions (`500`, hiding internal details outside `Development`) into RFC 9457 `ProblemDetails` with RFC 9110 `type` URIs plus `code`, `traceId`, and `correlationId`.
- **Logging & Correlation:** `CorrelationIdMiddleware` validates/propagates `X-Correlation-Id` without overwriting `HttpContext.TraceIdentifier` and opens an `ILogger` scope (`CorrelationId`, `TraceId`). Future sprints can plug OpenTelemetry tracing/metrics or approved structured log sinks into `Program.cs` without changing Application/Domain code.

---

## 6. Docker Build & Pending External Verification

> **Status: `BLOCKED — EXTERNAL VERIFICATION REQUIRED`**
> [Dockerfile](file:///C:/Users/Obad/Desktop/EduScroll/backend/Dockerfile) and [.dockerignore](file:///C:/Users/Obad/Desktop/EduScroll/backend/.dockerignore) were statically inspected and updated (`ENV HttpsRedirection__Enabled=false` for HTTP-only container ingress, no `rm -f` cleanup needed due to publish exclusions, non-root `$APP_UID` user, port `8080`), and `dotnet publish -c Release` succeeded. Because `docker` CLI is not installed on the development machine, the following verification commands **must** be executed on a Docker-enabled host or CI runner:

```bash
# 1. Build multi-stage image from backend/ directory
cd backend
docker build -t eduscroll-api:sprint1 -f Dockerfile .

# 2. Run container in Production mode (port 8080)
docker run -d --name eduscroll-api-verify -p 8080:8080 eduscroll-api:sprint1

# 3. Verify non-root user and absence of dev/test/example configs inside container
docker exec eduscroll-api-verify id
docker exec eduscroll-api-verify ls -la /app

# 4. Verify live endpoints, Production OpenAPI suppression, and clean logs (zero HTTPS redirect warnings)
curl -i http://localhost:8080/health/live
curl -i http://localhost:8080/health/ready
curl -i http://localhost:8080/openapi/v1.json   # Expect 404 application/problem+json
docker logs eduscroll-api-verify

# 5. Cleanup
docker stop eduscroll-api-verify && docker rm eduscroll-api-verify
```

---

## 7. Testing Instructions

```bash
dotnet test backend/EduScroll.sln
```
Includes:
- `EduScroll.Domain.Tests`: Domain isolation & exception invariants.
- `EduScroll.Application.Tests`: Application dependency boundaries, `AddApplication()`, and exception hierarchy.
- `EduScroll.Architecture.Tests`: Compiled assembly + `.csproj` `<ProjectReference>` Clean Architecture rules, acyclic DAG check, `Domain` framework isolation, and recursive non-composition API type inspection.
- `EduScroll.API.IntegrationTests`: `WebApplicationFactory<Program>` tests for `/health/live`, `/health/ready` (`200` and `503 ProblemDetails`), `X-Correlation-Id` & `traceId` consistency, `404` & all exception `ProblemDetails` across `Development` vs `Production`, OpenAPI schema/Dev-only exposure, `AllowedHosts` rejection, explicit CORS & startup validation, and `AddInfrastructure` idempotency.
