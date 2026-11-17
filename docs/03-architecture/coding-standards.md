# EduScroll — Backend Coding Standards

- **Document Version:** 1.0
- **Status:** Approved for Sprint 1 Baseline
- **Last Updated:** 2026-11-17

---

## 1. Compiler & Language Baseline
- **Target Framework:** `.NET 10` (`net10.0`) enforced via [Directory.Build.props](file:///C:/Users/Obad/Desktop/EduScroll/backend/Directory.Build.props).
- **Nullable Reference Types:** Enabled globally (`<Nullable>enable</Nullable>`).
- **Warnings as Errors:** Enabled globally (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`). Do not suppress compiler warnings without explicit inline justification.
- **Deterministic Builds:** Enabled (`<Deterministic>true</Deterministic>`).

## 2. Clean Architecture & Naming Rules
1. Follow standard C# PascalCase for public types/members, camelCase for parameters/locals, and `_camelCase` for private fields (when not using primary constructor captures).
2. Keep `EduScroll.Domain` dependency-free from Application, Infrastructure, API, and external frameworks (`NFR-005`).
3. Keep `EduScroll.Application` decoupled from Infrastructure and HTTP transport (`HttpContext`, `IActionResult`, status codes).
4. Place zero business logic in API controllers (`Constraint #9`).
5. Prefer simple, readable code over speculative abstractions; never add interfaces without an architectural boundary responsibility.

## 3. Reliability, Async & Hygiene
1. Use `async`/`await` for I/O-bound paths and accept `CancellationToken` on asynchronous methods.
2. Validate public/guard arguments eagerly (`ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrWhiteSpace`).
3. Never commit secrets, passwords, API keys, or local `.env` files.
4. Never log passwords, tokens, API keys, PII, or full source text (`SEC-012`).
5. Do not leave `TODO` markers pretending unfinished work is complete; explicitly document future sprint extension points instead.
