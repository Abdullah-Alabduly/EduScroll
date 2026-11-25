import 'package:dio/dio.dart';
import 'package:eduscroll/app/config/app_config.dart';
import 'package:eduscroll/core/constants/api_endpoints.dart';
import 'package:eduscroll/core/error/app_failure.dart';
import 'package:eduscroll/core/network/api_client.dart';
import 'package:eduscroll/core/storage/token_storage.dart';
import 'package:eduscroll/features/authentication/data/services/auth_api_service.dart';
import 'package:eduscroll/features/authentication/data/services/session_refresher.dart';
import 'package:flutter_test/flutter_test.dart';

import '../helpers/test_fakes.dart';

void main() {
  final baseTime = DateTime.utc(2026, 11, 25, 18);

  group('SessionRefresher & AuthInterceptor', () {
    test('coalesces concurrent 401 responses into a single refresh call and retries GET requests', () async {
      final tokenStorage = SecureTokenStorage(InMemorySecureStorage());
      await tokenStorage.saveTokens(
        sampleTokens(
          accessToken: 'stale-access',
          refreshToken: 'refresh-1',
          expiresAtUtc: baseTime.add(const Duration(minutes: 10)),
        ),
      );

      var refreshCalls = 0;
      final adapter = MockHttpClientAdapter((options, _) async {
        if (options.path == ApiEndpoints.refresh) {
          refreshCalls++;
          // Simulate brief network latency so all concurrent 401s overlap
          await Future<void>.delayed(const Duration(milliseconds: 30));
          return MockHttpClientAdapter.jsonResponse(
            sampleAuthResponseJson(
              accessToken: 'fresh-access',
              refreshToken: 'refresh-2',
              expiresAtUtc: baseTime.add(const Duration(minutes: 25)),
            ),
          );
        }

        if (options.path == ApiEndpoints.me) {
          if (options.headers['Authorization'] == 'Bearer fresh-access') {
            return MockHttpClientAdapter.jsonResponse(sampleUserJson());
          }
          return MockHttpClientAdapter.jsonResponse(
            {'status': 401, 'code': 'UNAUTHORIZED'},
            statusCode: 401,
          );
        }

        return MockHttpClientAdapter.jsonResponse({}, statusCode: 404);
      });

      final dio = ApiClientFactory.create(
        config: const AppConfig(
          environment: AppEnvironment.testing,
          apiBaseUrl: 'http://localhost:5221',
        ),
        tokenStorage: tokenStorage,
        nowProvider: () => baseTime,
        httpClientAdapter: adapter,
        refresherFactory: (client) => SessionRefresher(
          tokenStorage: tokenStorage,
          nowProvider: () => baseTime,
          refreshExecutor: (rt) => AuthApiService(client).refresh(rt),
        ),
      );

      final api = AuthApiService(dio);
      final profiles = await Future.wait([
        api.getCurrentUser(),
        api.getCurrentUser(),
        api.getCurrentUser(),
        api.getCurrentUser(),
        api.getCurrentUser(),
      ]);

      expect(profiles, hasLength(5));
      expect(refreshCalls, 1, reason: 'Only one single-flight refresh should run');

      final stored = await tokenStorage.readTokens();
      expect(stored?.accessToken, 'fresh-access');
      expect(stored?.refreshToken, 'refresh-2');
    });

    test('never refreshes for login 401 and prevents infinite retry loops', () async {
      final tokenStorage = SecureTokenStorage(InMemorySecureStorage());
      await tokenStorage.saveTokens(
        sampleTokens(
          accessToken: 'access-1',
          refreshToken: 'refresh-1',
          expiresAtUtc: baseTime.add(const Duration(minutes: 10)),
        ),
      );

      var refreshCalls = 0;
      final adapter = MockHttpClientAdapter((options, _) async {
        if (options.path == ApiEndpoints.refresh) {
          refreshCalls++;
          return MockHttpClientAdapter.jsonResponse(
            sampleAuthResponseJson(
              accessToken: 'still-rejected-access',
              refreshToken: 'refresh-2',
              expiresAtUtc: baseTime.add(const Duration(minutes: 25)),
            ),
          );
        }
        return MockHttpClientAdapter.jsonResponse(
          {'status': 401, 'code': 'INVALID_CREDENTIALS'},
          statusCode: 401,
        );
      });

      final dio = ApiClientFactory.create(
        config: const AppConfig(
          environment: AppEnvironment.testing,
          apiBaseUrl: 'http://localhost:5221',
        ),
        tokenStorage: tokenStorage,
        nowProvider: () => baseTime,
        httpClientAdapter: adapter,
        refresherFactory: (client) => SessionRefresher(
          tokenStorage: tokenStorage,
          nowProvider: () => baseTime,
          refreshExecutor: (rt) => AuthApiService(client).refresh(rt),
        ),
      );

      // 1. Login 401 must NEVER invoke refresh
      await expectLater(
        () => dio.post<dynamic>(
          ApiEndpoints.login,
          data: {'email': 'a@b.com', 'password': 'wrong'},
        ),
        throwsA(isA<DioException>()),
      );
      expect(refreshCalls, 0);

      // 2. Protected GET returning 401 even after refresh refreshes AT MOST ONCE
      await expectLater(
        () => AuthApiService(dio).getCurrentUser(),
        throwsA(isA<AppFailure>()),
      );
      expect(refreshCalls, 1);
    });

    test('clears stored tokens and notifies onSessionExpired when refresh fails with 401', () async {
      final tokenStorage = SecureTokenStorage(InMemorySecureStorage());
      await tokenStorage.saveTokens(
        sampleTokens(
          accessToken: 'expired-access',
          refreshToken: 'revoked-refresh',
          expiresAtUtc: baseTime.subtract(const Duration(minutes: 1)),
        ),
      );

      AppFailure? expiredFailure;
      final adapter = MockHttpClientAdapter((options, _) async {
        return MockHttpClientAdapter.jsonResponse(
          {'status': 401, 'code': 'REFRESH_TOKEN_REVOKED'},
          statusCode: 401,
        );
      });

      final dio = ApiClientFactory.create(
        config: const AppConfig(
          environment: AppEnvironment.testing,
          apiBaseUrl: 'http://localhost:5221',
        ),
        tokenStorage: tokenStorage,
        nowProvider: () => baseTime,
        httpClientAdapter: adapter,
        refresherFactory: (client) => SessionRefresher(
          tokenStorage: tokenStorage,
          nowProvider: () => baseTime,
          refreshExecutor: (rt) => AuthApiService(client).refresh(rt),
          onSessionExpired: (failure) => expiredFailure = failure,
        ),
      );

      await expectLater(
        () => AuthApiService(dio).getCurrentUser(),
        throwsA(
          isA<AppFailure>().having((f) => f.code, 'code', 'REFRESH_TOKEN_REVOKED'),
        ),
      );

      expect(await tokenStorage.readTokens(), isNull);
      expect(expiredFailure?.code, 'REFRESH_TOKEN_REVOKED');
    });

    test('does not blindly retry non-idempotent POST requests after 401', () async {
      final tokenStorage = SecureTokenStorage(InMemorySecureStorage());
      await tokenStorage.saveTokens(
        sampleTokens(
          accessToken: 'stale-access',
          refreshToken: 'refresh-1',
          expiresAtUtc: baseTime.add(const Duration(minutes: 10)),
        ),
      );

      var mutationCalls = 0;
      var refreshCalls = 0;
      final adapter = MockHttpClientAdapter((options, _) async {
        if (options.path == ApiEndpoints.refresh) {
          refreshCalls++;
          return MockHttpClientAdapter.jsonResponse(
            sampleAuthResponseJson(
              accessToken: 'fresh-access',
              refreshToken: 'refresh-2',
              expiresAtUtc: baseTime.add(const Duration(minutes: 25)),
            ),
          );
        }
        mutationCalls++;
        return MockHttpClientAdapter.jsonResponse(
          {'status': 401, 'code': 'UNAUTHORIZED'},
          statusCode: 401,
        );
      });

      final dio = ApiClientFactory.create(
        config: const AppConfig(
          environment: AppEnvironment.testing,
          apiBaseUrl: 'http://localhost:5221',
        ),
        tokenStorage: tokenStorage,
        nowProvider: () => baseTime,
        httpClientAdapter: adapter,
        refresherFactory: (client) => SessionRefresher(
          tokenStorage: tokenStorage,
          nowProvider: () => baseTime,
          refreshExecutor: (rt) => AuthApiService(client).refresh(rt),
        ),
      );

      await expectLater(
        () => dio.post<dynamic>('/api/v1/sources', data: {'title': 'Notes'}),
        throwsA(isA<DioException>()),
      );

      expect(refreshCalls, 1);
      expect(mutationCalls, 1, reason: 'Non-idempotent POST must not auto-retry');
    });
  });
}
