# EduScroll Mobile (Flutter)

Flutter mobile foundation for **EduScroll** (Sprint 3), integrated with the existing **ASP.NET Core 10 Web API** (`backend/`), **ASP.NET Core Identity**, and **JWT Bearer + Rotating Refresh Token** authentication.

---

## 1. Prerequisites & Verified SDK Versions

- **Flutter SDK:** `3.41.4` (stable channel) or compatible `3.x`
- **Dart SDK:** `3.11.1` (`sdk: ^3.11.1`)
- **Android Toolchain:** Android SDK (verified locally with platform `android-36`, build-tools `36.1.0`, JDK 21). If `flutter doctor` warns about unaccepted licenses, run:
  ```powershell
  flutter doctor --android-licenses
  ```
- **Backend Prerequisites:** Running `EduScroll.API` backed by Microsoft SQL Server (see root [README.md](../README.md)).

---

## 2. Architecture Overview

```text
mobile/lib/
├── main.dart                         # Entry point (AppConfig + ProviderScope)
├── app/
│   ├── app.dart                      # MaterialApp.router, light/dark themes, startup init
│   ├── config/app_config.dart        # Environment & API_BASE_URL config (--dart-define)
│   ├── router/app_router.dart        # GoRouter + centralized auth route guards
│   └── theme/                        # Design tokens (colors, spacing, Material 3 themes)
├── core/
│   ├── constants/api_endpoints.dart  # Verified /api/v1/auth/* and /health/* paths
│   ├── error/                        # AppFailure + RFC 9457 ProblemDetails ErrorMapper
│   ├── network/                      # Centralized Dio client, AuthInterceptor, CorrelationIdInterceptor
│   ├── storage/                      # SecureStorageService + SecureTokenStorage
│   ├── utils/validators.dart         # Client validators aligned with backend AuthRequestValidator
│   └── widgets/                      # Reusable UI components (buttons, fields, logo, error/empty views)
└── features/
    ├── authentication/               # Domain entities/repo, Data models/API/SessionRefresher, Riverpod controller & Splash/Login/Register screens
    ├── home/                         # Authenticated Home foundation screen (upcoming feed empty state)
    └── profile/                      # Live GET /api/v1/auth/me Profile screen
```

### Core Packages
- `dio` — single centralized HTTP client with timeouts, correlation IDs, and auth interceptor.
- `flutter_riverpod` — compile-safe dependency injection and reactive `AuthController` state.
- `go_router` — declarative navigation and centralized auth route guards.
- `flutter_secure_storage` — OS-encrypted token storage (Android Keystore AES-GCM/RSA-OAEP, iOS Keychain `first_unlock_this_device`, Windows Credential Manager).

---

## 3. Dependency Installation

```powershell
cd mobile
flutter pub get
```

---

## 4. Environment & API Configuration

The backend URL is **configuration** (never hardcoded across features and never a secret). Configure via `--dart-define`:

| Variable | Values | Default |
|---|---|---|
| `APP_ENV` | `development`, `testing`, `production` | `development` (debug/profile), `production` (release) |
| `API_BASE_URL` | Absolute `http://` or `https://` URL | `http://10.0.2.2:5221` on Android dev; `http://localhost:5221` on iOS/Windows/Web dev; **required `https://` in production** |

### Network & Platform Rules

- **Android Emulator:** `localhost` inside an Android emulator refers to the emulator itself, **not** your computer. Use `http://10.0.2.2:5221` (automatic default on Android debug builds).
- **Physical Android Device:** `localhost` and `10.0.2.2` do **not** point to your computer. Use your workstation's LAN IP (e.g., `http://192.168.1.25:5221`), bind the backend on `0.0.0.0`, include that IP in backend `AllowedHosts`, and allow port `5221` in Windows Defender Firewall.
- **HTTP vs. HTTPS Security:**
  - Production/Release Android builds use [android/app/src/main/res/xml/network_security_config.xml](android/app/src/main/res/xml/network_security_config.xml) with `cleartextTrafficPermitted="false"` (**HTTPS-only**).
  - Debug-only Android builds include [android/app/src/debug/res/xml/network_security_config.xml](android/app/src/debug/res/xml/network_security_config.xml) allowing HTTP **only** for `10.0.2.2`, `localhost`, and `127.0.0.1` (if testing HTTP over a LAN IP on a physical device in debug mode, add that LAN IP temporarily to the debug config only—never to `main/`).
  - TLS certificate validation is **never** disabled.
  - `AppConfig` throws a `StateError` if `APP_ENV=production` is started without an explicit `https://` `API_BASE_URL`.

---

## 5. Starting the Backend for Mobile Testing

### For Windows / iOS Simulator / Web (`localhost:5221`)
```powershell
sqllocaldb start MSSQLLocalDB
cd backend
dotnet run --project src/EduScroll.API/EduScroll.API.csproj --launch-profile http
```

### For Android Emulator (`10.0.2.2:5221`)
The backend's `AllowedHosts` defaults to `localhost;127.0.0.1`. Include `10.0.2.2` (and disable HTTPS redirection while using the HTTP dev profile):
```powershell
sqllocaldb start MSSQLLocalDB
cd backend
$env:AllowedHosts = "localhost;127.0.0.1;10.0.2.2"
$env:HttpsRedirection__Enabled = "false"
dotnet run --project src/EduScroll.API/EduScroll.API.csproj --urls http://0.0.0.0:5221
```

### For a Physical Android Device on Local Wi-Fi (Example IP `192.168.1.25`)
```powershell
cd backend
$env:AllowedHosts = "localhost;127.0.0.1;192.168.1.25"
$env:HttpsRedirection__Enabled = "false"
dotnet run --project src/EduScroll.API/EduScroll.API.csproj --urls http://0.0.0.0:5221
```

---

## 6. Running & Building the Flutter App

```powershell
cd mobile

# Run on Android emulator (defaults to http://10.0.2.2:5221)
flutter run

# Run on Windows desktop (defaults to http://localhost:5221)
flutter run -d windows

# Run with an explicit API host
flutter run --dart-define=APP_ENV=development --dart-define=API_BASE_URL=http://192.168.1.25:5221

# Build Android debug APK
flutter build apk --debug

# Build Android production release APK (HTTPS API_BASE_URL required)
flutter build apk --release --dart-define=APP_ENV=production --dart-define=API_BASE_URL=https://api.eduscroll.example

# Build Windows desktop debug/release
flutter build windows --debug
```

---

## 7. Authentication & Token Behavior

- **Registration (`POST /api/v1/auth/register`):** Sends `fullName`, `userName`, `email`, and `password` (with client-side `confirmPassword` verification). Because the backend returns `201 Created` with `UserProfileDto` and does **not** issue tokens on registration, the app displays a confirmation banner and routes to Login with the registered email prefilled.
- **Login (`POST /api/v1/auth/login`):** Stores `accessToken`, `tokenType`, `expiresAtUtc`, `refreshToken`, and `refreshTokenExpiresAtUtc` atomically in OS secure storage via `SecureTokenStorage`. Passwords are never stored or logged.
- **Startup Session Recovery:** On launch, `SplashScreen` loads secure storage, checks backend-provided `expiresAtUtc` / `refreshTokenExpiresAtUtc`, refreshes if needed, and validates via `GET /api/v1/auth/me`. If the backend is unreachable while a valid local session exists, a retry screen is shown instead of silently wiping tokens.
- **Single-Flight Refresh Rotation (`POST /api/v1/auth/refresh`):** `SessionRefresher` coalesces simultaneous `401` / expired-token refresh triggers into a single HTTP refresh request so concurrent API calls never collide with the backend's SQL Server `rowversion` concurrency lock. Only idempotent methods (`GET`, `HEAD`, `OPTIONS`, `PUT`, `DELETE`) or explicitly opted-in calls retry automatically, at most once.
- **Logout (`POST /api/v1/auth/logout`):** Sends Bearer JWT + active `refreshToken` to revoke the server session, and **always** clears local secure storage and resets state to unauthenticated. If the server cannot be reached, the user is still signed out locally and receives an honest notice rather than claiming remote revocation succeeded.

---

## 8. Running Static Analysis & Tests

```powershell
cd mobile

# Static analysis (strict casts, strict inference, avoid_print, unawaited_futures)
flutter analyze

# Run all offline unit & widget tests
flutter test

# Run live integration test against a running EduScroll.API backend
$env:EDUSCROLL_LIVE_API_URL = "http://127.0.0.1:5221"
flutter test test/integration/real_backend_auth_test.dart
```

---

## 9. Known Limitations (Sprint 3 Scope)

- **Educational Features Deferred:** PDF extraction, URL submission, AI analysis, YouTube discovery, and the vertical short-video feed are intentionally deferred to Sprints 4–9.
- **Profile Editing:** `ProfileScreen` is read-only because the backend exposes `GET /api/v1/auth/me` only.
- **Localization:** UI copy is currently English with centralized error messages in `ErrorMapper` ready for `flutter_localizations` / Arabic ARB expansion.
- **Android Licenses:** Accepting Android SDK licenses (`flutter doctor --android-licenses`) is required on developer workstations before Gradle Android APK builds can download missing SDK components if needed.
