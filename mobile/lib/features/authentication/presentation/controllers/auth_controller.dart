import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/error/app_failure.dart';
import '../../../../core/error/error_mapper.dart';
import '../../domain/entities/auth_session.dart';
import '../../domain/entities/user_profile.dart';
import '../../domain/repositories/auth_repository.dart';
import '../providers/auth_providers.dart';
import 'auth_state.dart';

class AuthController extends Notifier<AuthState> {
  AuthRepository get _repository => ref.read(authRepositoryProvider);

  @override
  AuthState build() => const AuthState.initializing();

  Future<void> initialize() async {
    state = const AuthState.initializing();
    try {
      final user = await _repository.restoreSession();
      if (user != null) {
        state = AuthState.authenticated(user);
      } else {
        state = const AuthState.unauthenticated();
      }
    } catch (error) {
      final failure = ErrorMapper.fromError(error);
      if (failure.isAuthFailure) {
        state = AuthState.unauthenticated(errorMessage: failure.message);
      } else {
        state = AuthState.startupError(failure.message);
      }
    }
  }

  Future<void> login({
    required String email,
    required String password,
  }) async {
    state = AuthState.authenticating(prefilledEmail: email.trim());
    try {
      final session = await _repository.login(
        email: email,
        password: password,
      );
      state = AuthState.authenticated(session.user);
    } catch (error) {
      final failure = ErrorMapper.fromError(error);
      state = AuthState.unauthenticated(
        errorMessage: failure.message,
        prefilledEmail: email.trim(),
      );
      throw failure;
    }
  }

  Future<UserProfile> register({
    required String fullName,
    required String userName,
    required String email,
    required String password,
  }) async {
    state = const AuthState.authenticating();
    try {
      final profile = await _repository.register(
        fullName: fullName,
        userName: userName,
        email: email,
        password: password,
      );
      state = AuthState.unauthenticated(
        infoMessage:
            'Account created for ${profile.email}. Sign in to continue.',
        prefilledEmail: profile.email,
      );
      return profile;
    } catch (error) {
      final failure = ErrorMapper.fromError(error);
      state = AuthState.unauthenticated(errorMessage: failure.message);
      throw failure;
    }
  }

  Future<UserProfile> reloadCurrentUser() async {
    final previousUser = state.user;
    if (previousUser != null) {
      state = AuthState.refreshing(previousUser);
    }

    try {
      final profile = await _repository.getCurrentUser();
      state = AuthState.authenticated(profile);
      return profile;
    } catch (error) {
      final failure = ErrorMapper.fromError(error);
      if (failure.isAuthFailure) {
        await _repository.clearLocalSession();
        state = AuthState.unauthenticated(errorMessage: failure.message);
      } else if (previousUser != null) {
        state = AuthState.authenticated(previousUser);
      }
      throw failure;
    }
  }

  Future<LogoutResult> logout() async {
    final result = await _repository.logout();
    state = AuthState.unauthenticated(
      infoMessage: result.serverRevoked
          ? 'You have been signed out.'
          : result.warningMessage,
    );
    return result;
  }

  Future<void> signOutLocally() async {
    await _repository.clearLocalSession();
    state = const AuthState.unauthenticated(
      infoMessage: 'Signed out on this device.',
    );
  }

  void clearNotices() {
    if (state.status == AuthStatus.unauthenticated &&
        (state.errorMessage != null || state.infoMessage != null)) {
      state = AuthState.unauthenticated(prefilledEmail: state.prefilledEmail);
    }
  }

  void handleSessionRefreshed(AuthSession session) {
    if (state.isAuthenticated || state.status == AuthStatus.initializing) {
      state = AuthState.authenticated(session.user);
    }
  }

  void handleSessionExpired(AppFailure failure) {
    state = AuthState.unauthenticated(errorMessage: failure.message);
  }
}
