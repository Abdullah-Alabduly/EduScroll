import 'package:eduscroll/app/app.dart';
import 'package:eduscroll/app/config/app_config.dart';
import 'package:eduscroll/core/constants/api_endpoints.dart';
import 'package:eduscroll/core/network/api_client.dart';
import 'package:eduscroll/core/storage/token_storage.dart';
import 'package:eduscroll/features/authentication/data/services/auth_api_service.dart';
import 'package:eduscroll/features/authentication/data/services/session_refresher.dart';
import 'package:eduscroll/features/authentication/presentation/providers/auth_providers.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

import '../helpers/test_fakes.dart';

void main() {
  final fixedNow = DateTime.utc(2026, 11, 25, 18);

  List<Override> buildTestOverrides({
    required InMemorySecureStorage storage,
    required MockHttpClientAdapter adapter,
  }) {
    return [
      appConfigProvider.overrideWithValue(
        const AppConfig(
          environment: AppEnvironment.testing,
          apiBaseUrl: 'http://localhost:5221',
        ),
      ),
      clockProvider.overrideWithValue(() => fixedNow),
      secureStorageServiceProvider.overrideWithValue(storage),
      apiSessionBundleProvider.overrideWith((ref) {
        final config = ref.watch(appConfigProvider);
        final tokenStorage = ref.watch(tokenStorageProvider);
        final clock = ref.watch(clockProvider);

        late final SessionRefresher refresher;
        final dio = ApiClientFactory.create(
          config: config,
          tokenStorage: tokenStorage,
          nowProvider: clock,
          httpClientAdapter: adapter,
          refresherFactory: (client) {
            final api = AuthApiService(client);
            refresher = SessionRefresher(
              tokenStorage: tokenStorage,
              nowProvider: clock,
              refreshExecutor: (rt) => api.refresh(rt),
              onSessionRefreshed: (s) => ref
                  .read(authControllerProvider.notifier)
                  .handleSessionRefreshed(s),
              onSessionExpired: (f) => ref
                  .read(authControllerProvider.notifier)
                  .handleSessionExpired(f),
            );
            return refresher;
          },
        );
        ref.onDispose(dio.close);
        return ApiSessionBundle(dio: dio, refresher: refresher);
      }),
    ];
  }

  testWidgets('full app flow: startup -> login -> home -> profile -> logout clears tokens', (tester) async {
    final storage = InMemorySecureStorage();
    var logoutCalled = false;

    final adapter = MockHttpClientAdapter((options, _) async {
      if (options.path == ApiEndpoints.login) {
        return MockHttpClientAdapter.jsonResponse(
          sampleAuthResponseJson(
            accessToken: 'jwt-live',
            refreshToken: 'rt-live',
            expiresAtUtc: fixedNow.add(const Duration(minutes: 15)),
            user: sampleUserJson(fullName: 'Obad Learner'),
          ),
        );
      }
      if (options.path == ApiEndpoints.me) {
        expect(options.headers['Authorization'], 'Bearer jwt-live');
        return MockHttpClientAdapter.jsonResponse(
          sampleUserJson(fullName: 'Obad Learner'),
        );
      }
      if (options.path == ApiEndpoints.logout) {
        expect(options.headers['Authorization'], 'Bearer jwt-live');
        logoutCalled = true;
        return MockHttpClientAdapter.jsonResponse(
          {'message': 'Signed out successfully.'},
        );
      }
      return MockHttpClientAdapter.jsonResponse({}, statusCode: 404);
    });

    await tester.pumpWidget(
      ProviderScope(
        overrides: buildTestOverrides(storage: storage, adapter: adapter),
        child: const EduScrollApp(),
      ),
    );
    await tester.pumpAndSettle();

    // Lands on Login
    expect(find.text('Welcome back'), findsOneWidget);

    await tester.enterText(
      find.byKey(const Key('login_email_field')),
      'ada@example.com',
    );
    await tester.enterText(
      find.byKey(const Key('login_password_field')),
      'StrongPassword123!',
    );
    await tester.tap(find.byKey(const Key('login_submit_button')));
    await tester.pumpAndSettle();

    // Redirected to Home foundation
    expect(find.text('Welcome, Obad Learner'), findsOneWidget);
    expect(find.text('Educational discovery arrives next'), findsOneWidget);

    // Tokens persisted securely
    final tokenStorage = SecureTokenStorage(storage);
    expect((await tokenStorage.readTokens())?.refreshToken, 'rt-live');

    // Open Profile screen (triggers GET /api/v1/auth/me)
    await tester.tap(find.byKey(const Key('home_open_profile_button')));
    await tester.pumpAndSettle();

    expect(find.text('Profile'), findsOneWidget);
    expect(find.text('Obad Learner'), findsAtLeastNWidgets(1));
    expect(find.text('ada@example.com'), findsOneWidget);
    expect(find.text('Active'), findsOneWidget);

    // Logout from Profile
    await tester.drag(find.byType(ListView), const Offset(0, -300));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('profile_logout_button')));
    await tester.pumpAndSettle();

    // Confirm logout dialog
    await tester.tap(find.widgetWithText(FilledButton, 'Sign out'));
    await tester.pumpAndSettle();

    expect(logoutCalled, isTrue);
    expect(await tokenStorage.readTokens(), isNull);
    expect(find.text('Welcome back'), findsOneWidget);
  });

  testWidgets('app restart restores expired access token via refresh and lands directly on Home', (tester) async {
    final storage = InMemorySecureStorage();
    final tokenStorage = SecureTokenStorage(storage);
    await tokenStorage.saveTokens(
      sampleTokens(
        accessToken: 'expired-jwt',
        refreshToken: 'valid-rt-1',
        expiresAtUtc: fixedNow.subtract(const Duration(minutes: 5)),
        refreshTokenExpiresAtUtc: fixedNow.add(const Duration(days: 5)),
      ),
    );

    var refreshCount = 0;
    final adapter = MockHttpClientAdapter((options, _) async {
      if (options.path == ApiEndpoints.refresh) {
        refreshCount++;
        return MockHttpClientAdapter.jsonResponse(
          sampleAuthResponseJson(
            accessToken: 'rotated-jwt',
            refreshToken: 'valid-rt-2',
            expiresAtUtc: fixedNow.add(const Duration(minutes: 15)),
            user: sampleUserJson(fullName: 'Restored Learner'),
          ),
        );
      }
      if (options.path == ApiEndpoints.me) {
        expect(options.headers['Authorization'], 'Bearer rotated-jwt');
        return MockHttpClientAdapter.jsonResponse(
          sampleUserJson(fullName: 'Restored Learner'),
        );
      }
      return MockHttpClientAdapter.jsonResponse({}, statusCode: 404);
    });

    await tester.pumpWidget(
      ProviderScope(
        overrides: buildTestOverrides(storage: storage, adapter: adapter),
        child: const EduScrollApp(),
      ),
    );
    await tester.pumpAndSettle();

    expect(refreshCount, 1);
    expect(find.text('Welcome, Restored Learner'), findsOneWidget);
    expect((await tokenStorage.readTokens())?.refreshToken, 'valid-rt-2');
  });
}
