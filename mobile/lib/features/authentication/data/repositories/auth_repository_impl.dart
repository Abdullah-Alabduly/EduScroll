import '../../../../core/error/error_mapper.dart';
import '../../../../core/storage/token_storage.dart';
import '../../domain/entities/auth_session.dart';
import '../../domain/entities/user_profile.dart';
import '../../domain/repositories/auth_repository.dart';
import '../models/login_request_model.dart';
import '../models/register_request_model.dart';
import '../services/auth_api_service.dart';
import '../services/session_refresher.dart';

class AuthRepositoryImpl implements AuthRepository {
  AuthRepositoryImpl({
    required AuthApiService apiService,
    required TokenStorage tokenStorage,
    required SessionRefresher sessionRefresher,
    Clock? nowProvider,
  })  : _apiService = apiService,
        _tokenStorage = tokenStorage,
        _sessionRefresher = sessionRefresher,
        _nowProvider = nowProvider ?? _utcNow;

  final AuthApiService _apiService;
  final TokenStorage _tokenStorage;
  final SessionRefresher _sessionRefresher;
  final Clock _nowProvider;

  @override
  Future<UserProfile> register({
    required String fullName,
    required String userName,
    required String email,
    required String password,
  }) {
    return _apiService.register(
      RegisterRequestModel(
        fullName: fullName,
        userName: userName,
        email: email,
        password: password,
      ),
    );
  }

  @override
  Future<AuthSession> login({
    required String email,
    required String password,
  }) async {
    final session = await _apiService.login(
      LoginRequestModel(email: email, password: password),
    );
    await _tokenStorage.saveTokens(session.tokens);
    return session;
  }

  @override
  Future<AuthSession> refreshSession() => _sessionRefresher.refreshSession();

  @override
  Future<UserProfile> getCurrentUser() => _apiService.getCurrentUser();

  @override
  Future<UserProfile?> restoreSession() async {
    final tokens = await _tokenStorage.readTokens();
    if (tokens == null) {
      return null;
    }

    final now = _nowProvider();
    if (tokens.isRefreshTokenExpired(now: now)) {
      await _tokenStorage.clearTokens();
      return null;
    }

    try {
      if (tokens.isAccessTokenExpired(now: now)) {
        await _sessionRefresher.refreshSession();
      }
      return await _apiService.getCurrentUser();
    } catch (error) {
      final failure = ErrorMapper.fromError(error);
      if (failure.isAuthFailure) {
        await _tokenStorage.clearTokens();
        return null;
      }
      throw failure;
    }
  }

  @override
  Future<LogoutResult> logout() async {
    var tokens = await _tokenStorage.readTokens();
    if (tokens == null) {
      return const LogoutResult(serverRevoked: true);
    }

    var serverRevoked = false;
    String? warningMessage;

    try {
      final now = _nowProvider();
      if (tokens.isAccessTokenExpired(now: now) &&
          !tokens.isRefreshTokenExpired(now: now)) {
        final refreshed = await _sessionRefresher.refreshSession();
        tokens = refreshed.tokens;
      }

      await _apiService.logout(tokens.refreshToken);
      serverRevoked = true;
    } catch (error) {
      final failure = ErrorMapper.fromError(error);
      serverRevoked = false;
      warningMessage = failure.isNetworkOrUnavailable
          ? 'Signed out on this device. Could not reach the server to revoke the remote session.'
          : 'Signed out on this device. Remote session revocation could not be confirmed.';
    } finally {
      await _tokenStorage.clearTokens();
    }

    return LogoutResult(
      serverRevoked: serverRevoked,
      warningMessage: warningMessage,
    );
  }

  @override
  Future<void> clearLocalSession() => _tokenStorage.clearTokens();

  static DateTime _utcNow() => DateTime.now().toUtc();
}
