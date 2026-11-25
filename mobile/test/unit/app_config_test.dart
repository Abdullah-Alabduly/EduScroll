import 'package:eduscroll/app/config/app_config.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('AppConfig', () {
    test('uses Android emulator loopback default in development on Android', () {
      final config = AppConfig.fromEnvironment(
        environmentOverride: AppEnvironment.development,
        baseUrlOverride: '',
        platformOverride: TargetPlatform.android,
        isWebOverride: false,
      );

      expect(config.isDevelopment, isTrue);
      expect(config.apiBaseUrl, 'http://10.0.2.2:5221');
    });

    test('uses localhost default in development on non-Android platforms', () {
      final config = AppConfig.fromEnvironment(
        environmentOverride: AppEnvironment.development,
        baseUrlOverride: '',
        platformOverride: TargetPlatform.windows,
        isWebOverride: false,
      );

      expect(config.apiBaseUrl, 'http://localhost:5221');
    });

    test('normalizes trailing slashes from API_BASE_URL', () {
      final config = AppConfig.fromEnvironment(
        environmentOverride: AppEnvironment.development,
        baseUrlOverride: 'http://192.168.1.50:5221///',
      );

      expect(config.apiBaseUrl, 'http://192.168.1.50:5221');
    });

    test('requires explicit HTTPS URL in production', () {
      expect(
        () => AppConfig.fromEnvironment(
          environmentOverride: AppEnvironment.production,
          baseUrlOverride: '',
        ),
        throwsStateError,
      );

      expect(
        () => AppConfig.fromEnvironment(
          environmentOverride: AppEnvironment.production,
          baseUrlOverride: 'http://api.eduscroll.example',
        ),
        throwsStateError,
      );

      final prod = AppConfig.fromEnvironment(
        environmentOverride: AppEnvironment.production,
        baseUrlOverride: 'https://api.eduscroll.example/',
      );
      expect(prod.isProduction, isTrue);
      expect(prod.apiBaseUrl, 'https://api.eduscroll.example');
    });

    test('rejects invalid or non-HTTP URLs', () {
      expect(
        () => AppConfig.fromEnvironment(
          environmentOverride: AppEnvironment.development,
          baseUrlOverride: 'ftp://example.com',
        ),
        throwsStateError,
      );
      expect(
        () => AppConfig.fromEnvironment(
          environmentOverride: AppEnvironment.development,
          baseUrlOverride: 'not-a-url',
        ),
        throwsStateError,
      );
    });
  });
}
