import 'dart:io';

import 'package:eduscroll/app/config/app_config.dart';
import 'package:eduscroll/core/error/app_failure.dart';
import 'package:eduscroll/core/network/api_client.dart';
import 'package:eduscroll/core/storage/token_storage.dart';
import 'package:eduscroll/features/authentication/data/repositories/auth_repository_impl.dart';
import 'package:eduscroll/features/authentication/data/services/auth_api_service.dart';
import 'package:eduscroll/features/authentication/data/services/session_refresher.dart';
import 'package:eduscroll/features/authentication/domain/entities/auth_tokens.dart';
import 'package:flutter_test/flutter_test.dart';

import '../helpers/test_fakes.dart';

/// Live integration tests against the actual ASP.NET Core 10 EduScroll.API backend.
///
/// Executed when `EDUSCROLL_LIVE_API_URL` is set (e.g., `http://127.0.0.1:5299`);
/// skipped cleanly during offline `flutter test` runs.
void main() {
  final liveBaseUrl = Platform.environment['EDUSCROLL_LIVE_API_URL']?.trim();

  test(
    'end-to-end auth integration against real ASP.NET Core 10 backend',
    () async {
      final config = AppConfig(
        environment: AppEnvironment.testing,
        apiBaseUrl: liveBaseUrl!,
      );
      final secureStorage = InMemorySecureStorage();
      final tokenStorage = SecureTokenStorage(secureStorage);

      late final SessionRefresher refresher;
      final dio = ApiClientFactory.create(
        config: config,
        tokenStorage: tokenStorage,
        refresherFactory: (client) {
          final api = AuthApiService(client);
          refresher = SessionRefresher(
            tokenStorage: tokenStorage,
            refreshExecutor: (rt) => api.refresh(rt),
          );
          return refresher;
        },
      );
      addTearDown(dio.close);

      final apiService = AuthApiService(dio);
      final repo = AuthRepositoryImpl(
        apiService: apiService,
        tokenStorage: tokenStorage,
        sessionRefresher: refresher,
      );

      final unique = DateTime.now().microsecondsSinceEpoch.toString();
      final email = 'flutter.e2e.$unique@example.com';
      final userName = 'fl_$unique';
      const password = 'StrongPassword123!';

      // 1. Register -> 201 Created (UserProfileDto, no tokens stored yet)
      final registered = await repo.register(
        fullName: 'Flutter Integration Learner',
        userName: userName,
        email: email,
        password: password,
      );
      expect(registered.email, email);
      expect(registered.userName, userName);
      expect(registered.isActive, isTrue);
      expect(await tokenStorage.readTokens(), isNull);

      // 2. Duplicate registration -> 409 DUPLICATE_EMAIL
      await expectLater(
        () => repo.register(
          fullName: 'Duplicate Learner',
          userName: 'dup_$unique',
          email: email,
          password: password,
        ),
        throwsA(
          isA<AppFailure>().having((f) => f.code, 'code', 'DUPLICATE_EMAIL'),
        ),
      );

      // 3. Invalid login -> 401 INVALID_CREDENTIALS
      await expectLater(
        () => repo.login(email: email, password: 'WrongPassword123!'),
        throwsA(
          isA<AppFailure>()
              .having((f) => f.code, 'code', 'INVALID_CREDENTIALS'),
        ),
      );

      // 4. Valid login -> 200 OK + secure token storage persistence
      final session = await repo.login(email: email, password: password);
      expect(session.user.id, registered.id);
      expect(session.tokens.tokenType, 'Bearer');
      final persisted = await tokenStorage.readTokens();
      expect(persisted, isNotNull);
      expect(persisted!.accessToken, session.tokens.accessToken);

      // 5. GET /api/v1/auth/me with Bearer JWT
      final me = await repo.getCurrentUser();
      expect(me.id, registered.id);
      expect(me.fullName, 'Flutter Integration Learner');

      // 6. Session persistence / restart recovery
      final restored = await repo.restoreSession();
      expect(restored?.id, registered.id);

      // 7. Concurrent refresh protection against real SQL Server rowversion:
      //    Force local access token to appear expired and fire 4 parallel /me calls.
      final oldRefreshToken = persisted.refreshToken;
      await tokenStorage.saveTokens(
        AuthTokens(
          accessToken: persisted.accessToken,
          tokenType: persisted.tokenType,
          expiresAtUtc: DateTime.now().toUtc().subtract(const Duration(minutes: 1)),
          refreshToken: oldRefreshToken,
          refreshTokenExpiresAtUtc: persisted.refreshTokenExpiresAtUtc,
        ),
      );

      final concurrentProfiles = await Future.wait([
        repo.getCurrentUser(),
        repo.getCurrentUser(),
        repo.getCurrentUser(),
        repo.getCurrentUser(),
      ]);
      expect(concurrentProfiles, hasLength(4));
      for (final p in concurrentProfiles) {
        expect(p.id, registered.id);
      }

      final rotatedTokens = await tokenStorage.readTokens();
      expect(rotatedTokens, isNotNull);
      expect(rotatedTokens!.refreshToken, isNot(oldRefreshToken));

      // Old refresh token is rejected by real backend
      await expectLater(
        () => apiService.refresh(oldRefreshToken),
        throwsA(
          isA<AppFailure>()
              .having((f) => f.code, 'code', 'REFRESH_TOKEN_REVOKED'),
        ),
      );

      // 8. Logout -> revokes rotated refresh token on server and clears local storage
      final logoutResult = await repo.logout();
      expect(logoutResult.serverRevoked, isTrue);
      expect(await tokenStorage.readTokens(), isNull);

      // Revoked refresh token cannot refresh afterward
      await expectLater(
        () => apiService.refresh(rotatedTokens.refreshToken),
        throwsA(
          isA<AppFailure>()
              .having((f) => f.code, 'code', 'REFRESH_TOKEN_REVOKED'),
        ),
      );
    },
    skip: (liveBaseUrl == null || liveBaseUrl.isEmpty)
        ? 'Set EDUSCROLL_LIVE_API_URL to run live backend integration tests.'
        : false,
  );
}
