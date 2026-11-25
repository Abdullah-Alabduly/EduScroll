# EduScroll Backend — Project Build & Sprint 2 Completion Report

- **Project:** EduScroll (Educational Short-Video Discovery Platform)
- **Branch:** `feature/sprint-2-database-auth` (branched from `feature/sprint-1-backend-foundation` at `cda74ce`)
- **Date:** 2026-11-25
- **Runtime & Stack:** ASP.NET Core 10 (`net10.0`) Web API, Clean Architecture, Entity Framework Core 10 (`10.0.14`), Microsoft SQL Server, ASP.NET Core Identity, JWT Bearer Authentication, xUnit

---

## 1. Executive Summary

The EduScroll backend now delivers a complete, tested, production-oriented **Sprint 1 (Backend Foundation)** and **Sprint 2 (Database Infrastructure, Identity & Authentication)** implementation.

What is built and verified right now:
- **Clean Architecture solution** with strict automated dependency enforcement across `Domain`, `Application`, `Infrastructure`, and `API`.
- **Microsoft SQL Server persistence** via Entity Framework Core 10 (replacing earlier PostgreSQL planning assumptions), with initial migration `20261117203108_InitialIdentityAndAuth` generated, scripted, and verified against SQL Server 2025 Express LocalDB (`(localdb)\MSSQLLocalDB`).
- **ASP.NET Core Identity** with custom `ApplicationUser` (`FullName`, `CreatedAt`, `UpdatedAt`, `IsActive`), strong password policy, account lockout, and database-enforced unique email & username indexes.
- **Full JWT Bearer + Refresh Token Authentication API** (`/api/v1/auth/register`, `login`, `refresh`, `logout`, `me`) with SHA-256 hashed refresh tokens, atomic token rotation, reuse rejection, and SQL Server `rowversion` concurrency protection.
- **Operational Readiness & Observability:** Separate `/health/live` (process liveness) and `/health/ready` (foundation + SQL Server readiness) probes, `X-Correlation-Id` tracking, standardized RFC 9457 `ProblemDetails`, explicit CORS, host filtering, startup-validated configuration, and OpenAPI/Swagger UI with working `Bearer` authentication (`[{"Bearer":[]}]`).
- **114 / 114 automated tests passing** (0 failed, 0 skipped) plus verified Release build, Release publish, EF Core CLI migrations, and live Kestrel end-to-end testing.

---

## 2. Solution Architecture & Project Layout

```mermaid
flowchart LR
    API["EduScroll.API<br/>(Controllers, Middleware, OpenAPI, Health)"] -->|Calls IAuthService / IDatabaseReadinessProbe| APP["EduScroll.Application<br/>(Use Cases, Validation, DTOs, Ports)"]
    INFRA["EduScroll.Infrastructure<br/>(EF Core SQL Server, Identity, JWT, Stores)"] -->|Implements Ports| APP
    INFRA -->|Persists Entities| DOM["EduScroll.Domain<br/>(RefreshToken, DomainException)"]
    APP -->|Uses Entities & Rules| DOM
    API -.->|Composition Root Only (Program.cs)| INFRA
```

### Directory Structure

```text
EduScroll/
├── .gitignore
├── README.md
├── docs/
│   ├── SPRINT-2-REPORT.md                 # This report
│   └── 03-architecture/
│       ├── backend-foundation-guide.md    # Updated for Sprint 2 + SQL Server
│       └── coding-standards.md
└── backend/
    ├── EduScroll.sln
    ├── Directory.Build.props              # net10.0, Nullable, ImplicitUsings, TreatWarningsAsErrors, Deterministic
    ├── dotnet-tools.json                  # Local EF Core CLI manifest (dotnet-ef 10.0.14)
    ├── Dockerfile & .dockerignore         # Multi-stage container definition (external Docker host required)
    ├── src/
    │   ├── EduScroll.Domain/
    │   ├── EduScroll.Application/
    │   ├── EduScroll.Infrastructure/
    │   └── EduScroll.API/
    └── tests/
        ├── EduScroll.Domain.Tests/
        ├── EduScroll.Application.Tests/
        ├── EduScroll.Architecture.Tests/
        └── EduScroll.API.IntegrationTests/
```

### Layer Breakdown

| Layer | Responsibility | Key Components Built |
|---|---|---|
| **Domain** (`EduScroll.Domain`) | Core domain invariants; **zero** NuGet or framework dependencies | `RefreshToken` entity (`Create`, `IsExpired`, `IsRevoked`, `IsActive`, `Revoke`), `DomainException`, `InvalidRefreshTokenDomainException` |
| **Application** (`EduScroll.Application`) | Transport-agnostic business logic, validation, DTOs, and port interfaces | `AuthService` (`IAuthService`), `AuthRequestValidator`, Contracts (`RegisterRequest`, `LoginRequest`, `RefreshTokenRequest`, `LogoutRequest`, `AuthResponse`, `UserProfileDto`, `MessageResponse`), Ports (`IUserIdentityStore`, `IRefreshTokenStore`, `ITokenService`, `IDatabaseReadinessProbe`), Exceptions (`AppException`, `ValidationAppException`, `UnauthorizedAppException`, `ForbiddenAppException`, `ConflictAppException`, `NotFoundAppException`) |
| **Infrastructure** (`EduScroll.Infrastructure`) | SQL Server persistence, Identity, JWT cryptography, and health probe adapter | `ApplicationDbContext`, `ApplicationUser`, `ApplicationUserConfiguration`, `RefreshTokenConfiguration`, `DesignTimeDbContextFactory`, `IdentityUserStore`, `RefreshTokenStore`, `JwtTokenService`, `SqlServerReadinessProbe`, `DatabaseOptions` + `DatabaseOptionsValidator`, `JwtOptions` + `JwtOptionsValidator`, `ConfigureJwtBearerOptions`, EF Core migrations |
| **API** (`EduScroll.API`) | Composition root, HTTP controllers, middleware, error mapping, OpenAPI/Swagger | `Program.cs`, `AuthController`, `HealthController`, `SelfLivenessCheck`, `FoundationReadinessCheck`, `DatabaseReadinessCheck`, `GlobalExceptionHandler`, `CorrelationIdMiddleware`, `ApiMetadataOptions`, `CorsOptions` + `CorsOptionsValidator` |

---

## 3. Database Infrastructure & Schema (Microsoft SQL Server)

### Provider & Resilience Configuration
- **Packages:** `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Design` (all `10.0.14`), plus patched `System.Security.Cryptography.Pkcs` (`10.0.14`) to resolve transitive `NU1902`/`NU1903` audit advisories.
- **SQL Server Options:** `CommandTimeout(30)` and `EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: 5s)`.
- **Startup Validation:** `DatabaseOptionsValidator` enforces non-empty, non-placeholder, syntactically valid SQL Server connection strings with a required `Server`/`Data Source` via `SqlConnectionStringBuilder`.
- **Design-Time Tooling:** `DesignTimeDbContextFactory` follows standard ASP.NET Core configuration precedence (`appsettings.json` -> `appsettings.{Environment}.json` -> `.NET User Secrets` in Development -> Environment Variables) so `dotnet ef` CLI commands work reliably without requiring runtime JWT secrets.

### Migration & Tables
- **Migration:** `backend/src/EduScroll.Infrastructure/Persistence/Migrations/20261117203108_InitialIdentityAndAuth.cs`
- **Identity Tables:** `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims`
- **Custom `AspNetUsers` Columns & Indexes:**
  - `Id` (`uniqueidentifier`, PK)
  - `FullName` (`nvarchar(200) NOT NULL`) — full Unicode/UTF-8 support
  - `UserName` / `NormalizedUserName` (`nvarchar(256) NOT NULL`, unique index `UserNameIndex`)
  - `Email` / `NormalizedEmail` (`nvarchar(256) NOT NULL`, unique index `EmailIndex`)
  - `CreatedAt` (`datetimeoffset NOT NULL`)
  - `UpdatedAt` (`datetimeoffset NOT NULL`)
  - `IsActive` (`bit NOT NULL DEFAULT 1`)
- **`RefreshTokens` Table:**
  - `Id` (`uniqueidentifier`, PK)
  - `UserId` (`uniqueidentifier NOT NULL`, FK -> `AspNetUsers.Id`, `ON DELETE CASCADE`)
  - `TokenHash` (`varchar(64) NOT NULL`, unique index `IX_RefreshTokens_TokenHash`)
  - `CreatedAt` (`datetimeoffset NOT NULL`)
  - `ExpiresAt` (`datetimeoffset NOT NULL`)
  - `RevokedAt` (`datetimeoffset NULL`)
  - `ReplacedByTokenHash` (`varchar(64) NULL`)
  - `RevocationReason` (`nvarchar(128) NULL`)
  - `RowVersion` (`rowversion`, SQL Server optimistic concurrency token)
  - Composite index `IX_RefreshTokens_UserId_RevokedAt_ExpiresAt`

---

## 4. API Endpoints Built

### Health & Documentation Endpoints

| Method & Route | Auth | Description | Responses |
|---|---|---|---|
| `GET /health/live` | Public | Process liveness probe (`SelfLivenessCheck`). Stays `200 Healthy` even during a SQL Server outage. | `200 OK` (`{"status":"Healthy"}`) |
| `GET /health/ready` | Public | Readiness probe (`FoundationReadinessCheck` + `DatabaseReadinessCheck`). Verifies SQL Server connectivity. | `200 OK` or `503 Service Unavailable` (`ProblemDetails`) |
| `GET /openapi/v1.json` | Dev only | OpenAPI v1 specification documenting all endpoints, `ProblemDetails` extensions, and `Bearer` security. Hidden (`404`) in Production/Testing. | `200 OK` (Dev), `404` (Prod/Testing) |
| `GET /swagger` | Dev only | Interactive Swagger UI backed by `/openapi/v1.json`. | `200 OK` (Dev), `404` (Prod/Testing) |

### Authentication Endpoints (`/api/v1/auth`)

| Method & Route | Auth | Request Payload | Success Response | Error Responses (`ProblemDetails`) |
|---|---|---|---|---|
| `POST /api/v1/auth/register` | Public | `{ fullName, userName, email, password }` | `201 Created` (`UserProfileDto`) + `Location: .../api/v1/auth/me` | `400 VALIDATION_FAILED`, `409 DUPLICATE_EMAIL` / `DUPLICATE_USERNAME` |
| `POST /api/v1/auth/login` | Public | `{ email, password }` | `200 OK` (`AuthResponse`: `accessToken`, `tokenType`, `expiresAtUtc`, `refreshToken`, `refreshTokenExpiresAtUtc`, `user`) | `400 VALIDATION_FAILED`, `401 INVALID_CREDENTIALS`, `403 ACCOUNT_INACTIVE` / `ACCOUNT_LOCKED` |
| `POST /api/v1/auth/refresh` | Public | `{ refreshToken }` | `200 OK` (`AuthResponse`: new JWT + rotated refresh token) | `400 VALIDATION_FAILED`, `401 INVALID_REFRESH_TOKEN` / `REFRESH_TOKEN_EXPIRED` / `REFRESH_TOKEN_REVOKED` / `REFRESH_TOKEN_REUSED`, `403 ACCOUNT_INACTIVE` |
| `POST /api/v1/auth/logout` | Bearer JWT | `{ refreshToken }` | `200 OK` (`{ "message": "Signed out successfully." }`) | `400 VALIDATION_FAILED`, `401 UNAUTHORIZED` / `INVALID_REFRESH_TOKEN` |
| `GET /api/v1/auth/me` | Bearer JWT | — | `200 OK` (`UserProfileDto` without sensitive Identity fields) | `401 UNAUTHORIZED` / `USER_NOT_FOUND`, `403 ACCOUNT_INACTIVE` |

---

## 5. Security & Cross-Cutting Controls

- **Password Security:** ASP.NET Core Identity salted adaptive PBKDF2 hashing (`UserManager`); policy requires 8–128 characters, uppercase, lowercase, digit, non-alphanumeric, and at least 6 unique characters.
- **Account Lockout:** Locks accounts for 15 minutes after 5 consecutive failed password attempts.
- **Timing-Mitigated Login:** Non-existent email lookups execute a dummy PBKDF2 verification to mitigate account-enumeration timing differences.
- **JWT Security:** HMAC-SHA256 (`ValidAlgorithms = [HmacSha256]`), strict validation of signing key (>= 32 UTF-8 bytes), issuer, audience, lifetime, expiration, and configurable clock skew (default `30s`), with `MapInboundClaims = false` and invariant-culture `iat`.
- **Refresh Token Security:** 512-bit CSPRNG opaque tokens (`RandomNumberGenerator.GetBytes(64)` -> Base64Url); only the 64-char SHA-256 hex digest is stored in SQL Server; single-use rotation inside a relational transaction with `rowversion` optimistic concurrency preventing concurrent double-spend; multi-device sessions are isolated per token.
- **Secret Hygiene:** Zero signing keys or database passwords committed in any `appsettings*.json`; `UserSecretsId` configured on `EduScroll.API.csproj` for local development; startup validation rejects missing or placeholder secrets; non-production config files are excluded from Release publish output (`CopyToPublishDirectory="Never"`).
- **Consistent Error Handling & Observability:** Every `4xx`/`5xx` response returns RFC 9457 `application/problem+json` with `type`, `title`, `status`, `instance`, `code`, `traceId`, `correlationId` (`X-Correlation-Id`), camelCase validation keys, and zero leaked stack traces, SQL errors, or raw JSON parser internals.

---

## 6. Issues Found & Fixed During Full Audit

| # | Area | Issue Found | Fix Applied |
|---|---|---|---|
| 1 | OpenAPI / Swagger | `OpenApiSecuritySchemeReference("Bearer")` without document context serialized as `[{}]` instead of `[{"Bearer":[]}]` | Attached document-bound `OpenApiSecuritySchemeReference("Bearer", document)` in `AddDocumentTransformer` |
| 2 | EF Core Design-Time | `DesignTimeDbContextFactory` loaded env vars before `appsettings.Development.json` and skipped User Secrets | Reordered to `appsettings` -> `User Secrets` (Dev) -> `Environment Variables` |
| 3 | Validation & Errors | Empty `{}` returned PascalCase keys without `code`/`instance`; malformed JSON exposed parser internals; `401` lacked `code` | Added custom `InvalidModelStateResponseFactory` + `CustomizeProblemDetails` defaults |
| 4 | Email Validation | `parsed.Host.Contains('.')` accepted `user@domain.`, `user@domain..com`, `user@-bad.com`, `user@domain.c` | Added strict `IsValidEmailHost` label and consecutive-dot validation |
| 5 | DI Idempotency | Calling `AddInfrastructure` twice double-registered the `"Bearer"` auth scheme | Guarded `AddAuthentication().AddJwtBearer()` against duplicate `JwtBearerHandler` registration |
| 6 | SQL Retry Safety | `RefreshTokenStore.PersistRotationAsync` called `AddAsync(replacementToken)` unconditionally inside `EnableRetryOnFailure` | Guarded with `EntityState.Detached` check before `AddAsync` |
| 7 | Startup DB Validation | Malformed SQL Server connection strings bypassed startup validation | Added `SqlConnectionStringBuilder` syntax & `DataSource` checks in `DatabaseOptionsValidator` |
| 8 | Hardening & Logging | Shared `JwtSecurityTokenHandler` on singleton, non-invariant `iat`, missing timing mitigation on unknown email, silent `CanConnectAsync == false` | Per-call handler + `InvariantCulture`, dummy PBKDF2 check on unknown email, nested `SqlException` traversal, readiness failure warning log |

---

## 7. Build, Test & Live Verification Results

### Build & Publish
- `dotnet build backend/EduScroll.sln -c Release --no-restore`: **Succeeded — 0 Warnings, 0 Errors** (`TreatWarningsAsErrors=true`).
- `dotnet publish backend/src/EduScroll.API/EduScroll.API.csproj -c Release`: **Succeeded** — only `appsettings.json` included in publish output.

### Automated Test Suite (`dotnet test backend/EduScroll.sln --no-restore`)

| Test Project | Passed | Failed | Skipped | Coverage Scope |
|---|---:|---:|---:|---|
| `EduScroll.Domain.Tests` | 16 | 0 | 0 | Domain isolation, `DomainException`, `RefreshToken` creation/expiry/revocation |
| `EduScroll.Application.Tests` | 42 | 0 | 0 | Application isolation, DI, exceptions, `AuthRequestValidator` (incl. malformed emails/passwords), `AuthService` business rules |
| `EduScroll.Architecture.Tests` | 5 | 0 | 0 | Clean Architecture `.csproj` + assembly references, DAG acyclicity, API-to-Infrastructure isolation |
| `EduScroll.API.IntegrationTests` | 51 | 0 | 0 | JWT/Crypto & validators, Sprint 1 foundation HTTP/CORS/Host/ProblemDetails, OpenAPI `[{"Bearer":[]}]`, SQL Server migrations, `/health/live` vs `/health/ready` outage isolation, full `/api/v1/auth/*` flows, concurrent refresh rotation, lockout, multi-device sessions, Unicode names, deactivation |
| **Total** | **114** | **0** | **0** | **100% passing against real SQL Server 2025 LocalDB** |

### Live End-to-End Verification (Kestrel + SQL Server LocalDB)
Verified live against a clean temporary SQL Server database (`EduScroll_LiveAudit`, dropped after verification) using local User Secrets:
- `dotnet tool run dotnet-ef database update` & `migrations script --idempotent` succeeded (`exit 0`).
- `/health/live` (`200`), `/health/ready` (`200`), `/swagger/index.html` (`200`), `/openapi/v1.json` (`[{"Bearer":[]}]`).
- Complete HTTP walkthrough: `{}` validation (`400`), malformed JSON (`400`), invalid email (`400`), register (`201`), duplicate register (`409`), bad login (`401`), login (`200`), anonymous `/me` (`401`), authenticated `/me` (`200`), refresh rotation (`200`), reused refresh token (`401`), logout (`200`), post-logout refresh (`401`), and CORS preflight (`204`).
- Runtime server logs had **zero stderr output, zero unhandled exceptions, and zero EF Core savepoint warnings**.

---

## 8. Quick-Start Commands

```powershell
# 1. Start SQL Server LocalDB (Windows)
sqllocaldb start MSSQLLocalDB

# 2. Restore packages & EF Core local tool
cd backend
dotnet restore EduScroll.sln
dotnet tool restore

# 3. Configure local JWT secret (one-time, stored outside Git)
$key = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet user-secrets set "Jwt:SecretKey" $key --project src/EduScroll.API/EduScroll.API.csproj

# 4. Apply database migrations
dotnet tool run dotnet-ef database update --project src/EduScroll.Infrastructure --startup-project src/EduScroll.API

# 5. Run the API (Swagger at http://localhost:5221/swagger)
dotnet run --project src/EduScroll.API/EduScroll.API.csproj

# 6. Run all tests
dotnet test EduScroll.sln
```

---

## 9. Remaining / Future Sprint Items

- **Git Commit:** Changes are currently uncommitted on `feature/sprint-2-database-auth` for your review.
- **Docker Verification *(skipped/out of scope locally)*:** `Dockerfile` runtime build/test requires a Docker-enabled host or CI runner.
- **CI/CD Workflow:** `.github/workflows/` pipeline (build, test, secret scan) can be added when configuring remote CI.
- **Upcoming Sprints:** Sprint 3 (Flutter Foundation), Sprint 4 (Source Ingestion & SSRF-hardened URL Fetch), Sprint 5 (AI Content Analysis), Sprint 6–7 (YouTube Discovery & Ranking), and Sprint 8+ (Pipeline & Feed).
