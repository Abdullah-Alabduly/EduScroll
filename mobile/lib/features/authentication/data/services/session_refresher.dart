import 'dart:async';

import '../../../../core/error/app_failure.dart';
import '../../../../core/error/error_mapper.dart';
import '../../../../core/storage/token_storage.dart';
import '../../domain/entities/auth_session.dart';

typedef RefreshTokenCallback = Future<AuthSession> Function(String refreshToken);
typedef SessionRefreshedCallback = void Function(AuthSession session);
typedef SessionExpiredCallback = void Function(AppFailure failure);
typedef Clock = DateTime Function();

/// Coordinates single-flight refresh token rotation so concurrent 401 responses
/// trigger only one POST /api/v1/auth/refresh call against the backend.
class SessionRefresher {
  SessionRefresher({
    required TokenStorage tokenStorage,
    required RefreshTokenCallback refreshExecutor,
    Clock? nowProvider,
    this.onSessionRefreshed,
    this.onSessionExpired,
  })  : _tokenStorage = tokenStorage,
        _refreshExecutor = refreshExecutor,
        _nowProvider = nowProvider ?? _utcNow;

  final TokenStorage _tokenStorage;
  final RefreshTokenCallback _refreshExecutor;
  final Clock _nowProvider;
  SessionRefreshedCallback? onSessionRefreshed;
  SessionExpiredCallback? onSessionExpired;

  Future<AuthSession>? _inFlightRefresh;

  bool get isRefreshing => _inFlightRefresh != null;

  Future<AuthSession> refreshSession() {
    final existing = _inFlightRefresh;
    if (existing != null) {
      return existing;
    }

    final future = _executeRefresh();
    _inFlightRefresh = future;
    // Clear the single-flight lock once settled (errors propagate to callers).
    unawaited(
      future.then<void>(
        (_) => _clearInFlight(future),
        onError: (Object _) => _clearInFlight(future),
      ),
    );
    return future;
  }

  void _clearInFlight(Future<AuthSession> completed) {
    if (identical(_inFlightRefresh, completed)) {
      _inFlightRefresh = null;
    }
  }

  Future<AuthSession> _executeRefresh() async {
    final currentTokens = await _tokenStorage.readTokens();
    final now = _nowProvider();

    if (currentTokens == null || currentTokens.isRefreshTokenExpired(now: now)) {
      await _tokenStorage.clearTokens();
      const failure = AppFailure(
        type: AppFailureType.unauthorized,
        code: 'REFRESH_TOKEN_EXPIRED',
        statusCode: 401,
        message: 'Your session has expired. Please sign in again.',
      );
      onSessionExpired?.call(failure);
      throw failure;
    }

    try {
      final refreshed = await _refreshExecutor(currentTokens.refreshToken);
      await _tokenStorage.saveTokens(refreshed.tokens);
      onSessionRefreshed?.call(refreshed);
      return refreshed;
    } catch (error) {
      final failure = ErrorMapper.fromError(error);
      if (failure.isAuthFailure) {
        await _tokenStorage.clearTokens();
        onSessionExpired?.call(failure);
      }
      throw failure;
    }
  }

  static DateTime _utcNow() => DateTime.now().toUtc();
}
