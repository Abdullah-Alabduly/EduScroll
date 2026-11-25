import 'package:flutter/foundation.dart';

enum AppEnvironment {
  development,
  testing,
  production;

  static AppEnvironment parse(String raw) {
    switch (raw.trim().toLowerCase()) {
      case 'development':
      case 'dev':
        return AppEnvironment.development;
      case 'testing':
      case 'test':
        return AppEnvironment.testing;
      case 'production':
      case 'prod':
        return AppEnvironment.production;
      default:
        return kReleaseMode
            ? AppEnvironment.production
            : AppEnvironment.development;
    }
  }
}

/// Centralized, non-secret runtime configuration for the EduScroll client.
///
/// Configure via Dart defines, e.g.:
/// `flutter run --dart-define=APP_ENV=development --dart-define=API_BASE_URL=http://10.0.2.2:5221`
@immutable
class AppConfig {
  const AppConfig({
    required this.environment,
    required this.apiBaseUrl,
    this.connectTimeout = const Duration(seconds: 15),
    this.sendTimeout = const Duration(seconds: 15),
    this.receiveTimeout = const Duration(seconds: 20),
  });

  static const String _envDefine = String.fromEnvironment('APP_ENV');
  static const String _baseUrlDefine = String.fromEnvironment('API_BASE_URL');

  final AppEnvironment environment;
  final String apiBaseUrl;
  final Duration connectTimeout;
  final Duration sendTimeout;
  final Duration receiveTimeout;

  bool get isDevelopment => environment == AppEnvironment.development;
  bool get isTesting => environment == AppEnvironment.testing;
  bool get isProduction => environment == AppEnvironment.production;

  factory AppConfig.fromEnvironment({
    AppEnvironment? environmentOverride,
    String? baseUrlOverride,
    TargetPlatform? platformOverride,
    bool? isWebOverride,
  }) {
    final env = environmentOverride ??
        (_envDefine.isNotEmpty
            ? AppEnvironment.parse(_envDefine)
            : (kReleaseMode
                ? AppEnvironment.production
                : AppEnvironment.development));

    final rawUrl = (baseUrlOverride ?? _baseUrlDefine).trim();
    final isWeb = isWebOverride ?? kIsWeb;
    final platform = platformOverride ?? defaultTargetPlatform;

    final resolvedUrl = rawUrl.isNotEmpty
        ? _normalizeBaseUrl(rawUrl)
        : _defaultBaseUrlFor(
            environment: env,
            isWeb: isWeb,
            platform: platform,
          );

    validateBaseUrl(resolvedUrl, environment: env);

    return AppConfig(
      environment: env,
      apiBaseUrl: resolvedUrl,
    );
  }

  AppConfig copyWithBaseUrl(String newBaseUrl) {
    final normalized = _normalizeBaseUrl(newBaseUrl);
    validateBaseUrl(normalized, environment: environment);
    return AppConfig(
      environment: environment,
      apiBaseUrl: normalized,
      connectTimeout: connectTimeout,
      sendTimeout: sendTimeout,
      receiveTimeout: receiveTimeout,
    );
  }

  static String _defaultBaseUrlFor({
    required AppEnvironment environment,
    required bool isWeb,
    required TargetPlatform platform,
  }) {
    if (environment == AppEnvironment.production) {
      throw StateError(
        'API_BASE_URL must be explicitly provided via --dart-define=API_BASE_URL=https://... '
        'when running in production.',
      );
    }

    if (!isWeb && platform == TargetPlatform.android) {
      return 'http://10.0.2.2:5221';
    }

    return 'http://localhost:5221';
  }

  static String _normalizeBaseUrl(String url) {
    var trimmed = url.trim();
    while (trimmed.endsWith('/')) {
      trimmed = trimmed.substring(0, trimmed.length - 1);
    }
    return trimmed;
  }

  static void validateBaseUrl(
    String url, {
    required AppEnvironment environment,
  }) {
    final uri = Uri.tryParse(url);
    if (uri == null || !uri.hasScheme || uri.host.isEmpty) {
      throw StateError('Invalid API_BASE_URL: "$url". Expected an absolute URL.');
    }

    final scheme = uri.scheme.toLowerCase();
    if (scheme != 'http' && scheme != 'https') {
      throw StateError(
        'Unsupported scheme "$scheme" in API_BASE_URL. Use http or https.',
      );
    }

    if (environment == AppEnvironment.production && scheme != 'https') {
      throw StateError(
        'Production API_BASE_URL must use https://. Refusing insecure URL.',
      );
    }
  }
}
