import 'package:eduscroll/core/storage/token_storage.dart';
import 'package:flutter_test/flutter_test.dart';

import '../helpers/test_fakes.dart';

void main() {
  group('SecureTokenStorage & AuthTokens', () {
    test('persists, reads, updates, and clears tokens securely', () async {
      final secureStorage = InMemorySecureStorage();
      final tokenStorage = SecureTokenStorage(secureStorage);

      expect(await tokenStorage.readTokens(), isNull);

      final initial = sampleTokens();
      await tokenStorage.saveTokens(initial);
      final restored = await tokenStorage.readTokens();
      expect(restored, equals(initial));

      final rotated = sampleTokens(
        accessToken: 'access-v2',
        refreshToken: 'refresh-v2',
      );
      await tokenStorage.saveTokens(rotated);
      expect(await tokenStorage.readTokens(), equals(rotated));

      await tokenStorage.clearTokens();
      expect(await tokenStorage.readTokens(), isNull);
    });

    test('recovers cleanly from corrupted secure storage payload', () async {
      final secureStorage = InMemorySecureStorage();
      secureStorage.values['eduscroll.auth.tokens.v1'] = '{not-valid-json';
      final tokenStorage = SecureTokenStorage(secureStorage);

      final result = await tokenStorage.readTokens();

      expect(result, isNull);
      expect(secureStorage.values, isEmpty);
    });

    test('redacts raw tokens in AuthTokens.toString()', () {
      final tokens = sampleTokens(
        accessToken: 'super-secret-jwt-value',
        refreshToken: 'super-secret-refresh-value',
      );
      final rendered = tokens.toString();

      expect(rendered, isNot(contains('super-secret-jwt-value')));
      expect(rendered, isNot(contains('super-secret-refresh-value')));
      expect(rendered, contains('[REDACTED]'));
    });

    test('evaluates access and refresh token expiration using backend timestamps', () {
      final now = DateTime.utc(2026, 11, 25, 18);
      final tokens = sampleTokens(
        expiresAtUtc: now.add(const Duration(seconds: 25)),
        refreshTokenExpiresAtUtc: now.add(const Duration(days: 7)),
      );

      // Within 30-second safety buffer -> treated as expired for proactive refresh
      expect(tokens.isAccessTokenExpired(now: now), isTrue);
      // With zero buffer -> still valid
      expect(
        tokens.isAccessTokenExpired(now: now, buffer: Duration.zero),
        isFalse,
      );
      expect(tokens.isRefreshTokenExpired(now: now), isFalse);
      expect(
        tokens.isRefreshTokenExpired(now: now.add(const Duration(days: 8))),
        isTrue,
      );
    });
  });
}
