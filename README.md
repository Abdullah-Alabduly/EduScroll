# EduScroll

EduScroll is an educational short-video discovery platform built with an **ASP.NET Core 10 Web API** backend (`backend/`, Clean Architecture + EF Core 10 + Microsoft SQL Server + ASP.NET Core Identity + JWT Bearer) and a **Flutter** mobile application (`mobile/`, Dart + Riverpod + Dio + GoRouter + `flutter_secure_storage`).

> **Database Technology Notice:** Microsoft SQL Server is the authoritative relational database for EduScroll (replacing earlier PostgreSQL planning assumptions).

---

## Repository Structure

```text
EduScroll/
├── .gitignore
├── README.md
├── docs/                                  # Product, SRS, architecture & sprint reports
├── backend/
│   ├── EduScroll.sln                      # Backend .NET 10 solution
│   ├── Directory.Build.props              # Shared net10.0, Nullable, TreatWarningsAsErrors
│   ├── dotnet-tools.json                  # Local EF Core CLI tool manifest (dotnet-ef 10.0.14)
│   ├── Dockerfile                         # Multi-stage container build (requires Docker host)
│   ├── .dockerignore
│   ├── src/
│   │   ├── EduScroll.Domain/              # Pure domain entities & exceptions (no framework deps)
│   │   ├── EduScroll.Application/         # Auth use cases, DTOs, validation & ports
│   │   ├── EduScroll.Infrastructure/      # EF Core SQL Server, Identity, JWT, refresh tokens
│   │   └── EduScroll.API/                 # Web API host, AuthController, HealthController, OpenAPI
│   └── tests/
│       ├── EduScroll.Domain.Tests/
│       ├── EduScroll.Application.Tests/
│       ├── EduScroll.Architecture.Tests/
│       └── EduScroll.API.IntegrationTests/
└── mobile/                                # Flutter client foundation & auth integration (Sprint 3)
    ├── pubspec.yaml
    ├── README.md                          # Complete Flutter setup, Android & testing guide
    ├── lib/                               # App config, theme, router, core network/storage, auth/home/profile
    └── test/                              # Unit, widget & live backend integration tests
```

> **Flutter Mobile App Setup:** See [mobile/README.md](mobile/README.md) for Flutter SDK prerequisites (`3.41.4` / Dart `3.11.1`), Android emulator (`10.0.2.2:5221`) vs. physical device networking, `--dart-define` API configuration, and Flutter test/build commands.

---

## Prerequisites

- **.NET 10 SDK** (`10.0.301` or newer `10.0.x` patch).
- **Microsoft SQL Server** (any reachable SQL Server instance, e.g., Windows SQL Server Express LocalDB `(localdb)\MSSQLLocalDB`, SQL Server Developer/Express, or Azure SQL).
- **EF Core CLI (`dotnet-ef`)**: Configured via local tool manifest `backend/dotnet-tools.json` (`dotnet-ef` `10.0.14`).
- **Docker CLI *(optional / not installed locally)***: Not required for Sprint 2 local development, migrations, or test execution. Container image verification requires a Docker-enabled host or CI runner.

---

## Local Development Setup

### 1. Restore NuGet Packages & Local Tools

```powershell
cd backend
dotnet restore EduScroll.sln
dotnet tool restore
```

### 2. Configure SQL Server Connection

`backend/src/EduScroll.API/appsettings.Development.json` defaults to Windows LocalDB using Integrated Security (zero hardcoded credentials):

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=EduScroll_Dev;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=15"
}
```

If using LocalDB, ensure the instance is running:

```powershell
sqllocaldb start MSSQLLocalDB
```

To target a different SQL Server instance (or SQL authentication in non-LocalDB environments), override via **.NET User Secrets** or environment variables—never commit credentials to `appsettings*.json`:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=EduScroll_Dev;User Id=YOUR_USER;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=True;Connection Timeout=30" --project backend/src/EduScroll.API/EduScroll.API.csproj
```

### 3. Configure JWT Security Secrets

`Jwt:SecretKey` must be at least **32 UTF-8 bytes (256 bits)** and is **never** committed to source control. Set it locally with `.NET User Secrets`:

```powershell
dotnet user-secrets set "Jwt:SecretKey" "REPLACE_WITH_YOUR_RANDOM_32_PLUS_BYTE_LOCAL_DEV_KEY" --project backend/src/EduScroll.API/EduScroll.API.csproj
```

Non-sensitive JWT defaults live in `appsettings.json` / `appsettings.Development.json`:

| Setting | Default | Description |
|---|---|---|
| `Jwt:Issuer` | `EduScroll.API` | Expected `iss` claim |
| `Jwt:Audience` | `EduScroll.Clients` | Expected `aud` claim |
| `Jwt:SecretKey` | *(empty — required)* | HS256 signing key (min 32 bytes; inject via User Secrets or `Jwt__SecretKey`) |
| `Jwt:AccessTokenExpirationMinutes` | `15` | Access token lifetime (1–1440 min) |
| `Jwt:RefreshTokenExpirationDays` | `7` | Refresh token lifetime (1–90 days) |
| `Jwt:ClockSkewSeconds` | `30` | Allowed validation clock skew (0–300 sec) |

> **Production Configuration:** Both `ConnectionStrings:DefaultConnection` (`ConnectionStrings__DefaultConnection`) and `Jwt:SecretKey` (`Jwt__SecretKey`) are validated at startup via `IValidateOptions`. Missing or placeholder values fail startup immediately.

### 4. Apply Entity Framework Core Migrations

Run from the `backend/` directory:

```powershell
# Apply migrations to the configured SQL Server database
dotnet tool run dotnet-ef database update --project src/EduScroll.Infrastructure --startup-project src/EduScroll.API

# Add a new migration (future sprints)
dotnet tool run dotnet-ef migrations add <MigrationName> --project src/EduScroll.Infrastructure --startup-project src/EduScroll.API --output-dir Persistence/Migrations

# Generate idempotent SQL deployment script
dotnet tool run dotnet-ef migrations script --idempotent --project src/EduScroll.Infrastructure --startup-project src/EduScroll.API --output migrations.sql
```

### 5. Run the API

```powershell
dotnet run --project backend/src/EduScroll.API/EduScroll.API.csproj
```

Default Development URLs (`backend/src/EduScroll.API/Properties/launchSettings.json`):
- HTTP: `http://localhost:5221`
- HTTPS: `https://localhost:7017`
- OpenAPI Spec *(Development only)*: `/openapi/v1.json`
- Swagger UI *(Development only)*: `/swagger`

---

## Authentication & Health API Reference

All `4xx`/`5xx` error responses follow **RFC 9457 `ProblemDetails`** (`application/problem+json`) with `code`, `traceId`, and `correlationId` (`X-Correlation-Id`).

### Health Probes

| Method & Route | Auth | Purpose |
|---|---|---|
| `GET /health/live` | Public | Process liveness probe (`SelfLivenessCheck`). Stays `200 Healthy` even if SQL Server is unreachable. |
| `GET /health/ready` | Public | Readiness probe (`FoundationReadinessCheck` + `DatabaseReadinessCheck`). Returns `200 Healthy` when SQL Server is reachable, or `503` when unavailable. |

### Authentication Endpoints (`/api/v1/auth`)

| Method & Route | Auth | Request Body | Success Response | Error Responses |
|---|---|---|---|---|
| `POST /api/v1/auth/register` | Public | `{ "fullName", "userName", "email", "password" }` | `201 Created` (`UserProfileDto`) | `400` Validation, `409` Duplicate email/username |
| `POST /api/v1/auth/login` | Public | `{ "email", "password" }` | `200 OK` (`AuthResponse`: JWT + refresh token + `user`) | `400` Validation, `401` Invalid credentials, `403` Inactive or locked account |
| `POST /api/v1/auth/refresh` | Public | `{ "refreshToken" }` | `200 OK` (`AuthResponse`: new JWT + rotated refresh token) | `400` Validation, `401` Invalid/expired/revoked/reused token, `403` Inactive account |
| `POST /api/v1/auth/logout` | Bearer JWT | `{ "refreshToken" }` | `200 OK` (`{ "message": "Signed out successfully." }`) | `400` Validation, `401` Unauthorized or invalid token |
| `GET /api/v1/auth/me` | Bearer JWT | — | `200 OK` (`UserProfileDto`) | `401` Unauthorized, `403` Inactive account |

### Security Controls Implemented

- **Password Hashing & Policy:** ASP.NET Core Identity adaptive salted hashing (`PasswordHasher`), minimum 8 characters with uppercase, lowercase, digit, special character, and at least 6 unique characters.
- **Account Lockout:** Locks account for 15 minutes after 5 consecutive failed password attempts.
- **Refresh Token Security:** 512-bit CSPRNG opaque tokens; only the 64-char SHA-256 hex digest is persisted in SQL Server `RefreshTokens` (with unique index `IX_RefreshTokens_TokenHash`), rotated on every refresh, revoked on logout/rotation, and protected against concurrent refresh races via SQL Server `rowversion` optimistic concurrency inside a transaction.
- **No Sensitive Logging:** Passwords, raw refresh tokens, JWT signing keys, and connection strings are never logged or returned in API errors.

---

## Running Tests

```powershell
# Run full test suite (SQL Server integration tests automatically target LocalDB if available)
dotnet test backend/EduScroll.sln

# Optionally point integration tests to a specific SQL Server instance
$env:EDUSCROLL_TEST_SQLSERVER = "Server=localhost;Database=master;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
dotnet test backend/EduScroll.sln
```

If no SQL Server instance is reachable (e.g., on a host without LocalDB or SQL Server), SQL Server-dependent integration tests are explicitly skipped via `SkippableFact` with diagnostic output, while all Domain, Application, Architecture, JWT/Crypto, and non-DB API tests continue executing.
