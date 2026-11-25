import 'package:dio/dio.dart';

import '../../features/authentication/data/services/session_refresher.dart';
import '../constants/api_endpoints.dart';
import '../error/error_mapper.dart';
import '../storage/token_storage.dart';

/// Attaches Bearer JWT tokens to protected requests, proactively refreshes
/// expiring access tokens using backend expiration timestamps, and coordinates
/// single-flight 401 recovery without infinite loops or blind unsafe retries.
class AuthInterceptor extends Interceptor {
  AuthInterceptor({
    required Dio dio,
    required TokenStorage tokenStorage,
    required SessionRefresher sessionRefresher,
    Clock? nowProvider,
  })  : _dio = dio,
        _tokenStorage = tokenStorage,
        _sessionRefresher = sessionRefresher,
        _nowProvider = nowProvider ?? _utcNow;

  static const String skipAuthKey = 'skipAuth';
  static const String retryAfterRefreshKey = 'retryAfterRefresh';
  static const String _retriedKey = '_authRetried';

  static const Set<String> _idempotentMethods = {
    'GET',
    'HEAD',
    'OPTIONS',
    'PUT',
    'DELETE',
  };

  final Dio _dio;
  final TokenStorage _tokenStorage;
  final SessionRefresher _sessionRefresher;
  final Clock _nowProvider;

  @override
  Future<void> onRequest(
    RequestOptions options,
    RequestInterceptorHandler handler,
  ) async {
    if (_shouldSkipAuth(options)) {
      handler.next(options);
      return;
    }

    try {
      var tokens = await _tokenStorage.readTokens();
      if (tokens != null) {
        if (tokens.isAccessTokenExpired(now: _nowProvider())) {
          final refreshed = await _sessionRefresher.refreshSession();
          tokens = refreshed.tokens;
        }

        options.headers['Authorization'] =
            '${tokens.tokenType} ${tokens.accessToken}';
      }
      handler.next(options);
    } catch (error) {
      final failure = ErrorMapper.fromError(error);
      handler.reject(
        DioException(
          requestOptions: options,
          error: failure,
          type: DioExceptionType.unknown,
        ),
      );
    }
  }

  @override
  Future<void> onError(
    DioException err,
    ErrorInterceptorHandler handler,
  ) async {
    final options = err.requestOptions;
    final statusCode = err.response?.statusCode;

    if (statusCode != 401 ||
        _shouldSkipAuth(options) ||
        options.extra[_retriedKey] == true) {
      handler.next(err);
      return;
    }

    final storedTokens = await _tokenStorage.readTokens();
    if (storedTokens == null) {
      handler.next(err);
      return;
    }

    try {
      final refreshed = await _sessionRefresher.refreshSession();

      if (!_isEligibleForRetry(options)) {
        handler.next(err);
        return;
      }

      final retryHeaders = Map<String, dynamic>.from(options.headers);
      retryHeaders['Authorization'] =
          '${refreshed.tokens.tokenType} ${refreshed.tokens.accessToken}';

      final retryExtra = Map<String, dynamic>.from(options.extra);
      retryExtra[_retriedKey] = true;

      final retryOptions = options.copyWith(
        headers: retryHeaders,
        extra: retryExtra,
      );

      final response = await _dio.fetch<dynamic>(retryOptions);
      handler.resolve(response);
    } catch (refreshError) {
      final failure = ErrorMapper.fromError(refreshError);
      handler.reject(
        DioException(
          requestOptions: options,
          response: err.response,
          error: failure,
          type: DioExceptionType.badResponse,
        ),
      );
    }
  }

  bool _shouldSkipAuth(RequestOptions options) {
    if (options.extra[skipAuthKey] == true) {
      return true;
    }
    return _isPublicAuthPath(options.path);
  }

  bool _isPublicAuthPath(String path) {
    final normalized = path.split('?').first;
    for (final publicPath in ApiEndpoints.publicAuthPaths) {
      if (normalized == publicPath || normalized.endsWith(publicPath)) {
        return true;
      }
    }
    return false;
  }

  bool _isEligibleForRetry(RequestOptions options) {
    if (options.extra[retryAfterRefreshKey] == true) {
      return true;
    }
    return _idempotentMethods.contains(options.method.toUpperCase());
  }

  static DateTime _utcNow() => DateTime.now().toUtc();
}
