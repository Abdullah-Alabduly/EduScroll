import 'package:eduscroll/app/router/app_router.dart';
import 'package:eduscroll/core/error/app_failure.dart';
import 'package:eduscroll/features/authentication/domain/entities/auth_session.dart';
import 'package:eduscroll/features/authentication/domain/entities/user_profile.dart';
import 'package:eduscroll/features/authentication/domain/repositories/auth_repository.dart';
import 'package:eduscroll/features/authentication/presentation/controllers/auth_state.dart';
import 'package:eduscroll/features/authentication/presentation/providers/auth_providers.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

import '../helpers/test_fakes.dart';

class _FakeAuthRepository implements AuthRepository {
  UserProfile? restoredUser;
  Object? restoreError;
  AuthSession? loginSession;
  Object? loginError;
  UserProfile? registeredUser;
  Object? registerError;
  UserProfile? currentUser;
  Object? currentUserError;
  LogoutResult logoutResult = const LogoutResult(serverRevoked: true);
  bool clearedLocal = false;

  @override
  Future<UserProfile?> restoreSession() async {
    if (restoreError != null) throw restoreError!;
    return restoredUser;
  }

  @override
  Future<AuthSession> login({
    required String email,
    required String password,
  }) async {
    if (loginError != null) throw loginError!;
    return loginSession ?? sampleSession();
  }

  @override
  Future<UserProfile> register({
    required String fullName,
    required String userName,
    required String email,
    required String password,
  }) async {
    if (registerError != null) throw registerError!;
    return registeredUser ??
        sampleUser(fullName: fullName, userName: userName, email: email);
  }

  @override
  Future<AuthSession> refreshSession() async => sampleSession();

  @override
  Future<UserProfile> getCurrentUser() async {
    if (currentUserError != null) throw currentUserError!;
    return currentUser ?? sampleUser();
  }

  @override
  Future<LogoutResult> logout() async {
    clearedLocal = true;
    return logoutResult;
  }

  @override
  Future<void> clearLocalSession() async {
    clearedLocal = true;
  }
}

void main() {
  group('AuthController', () {
    test('startup transitions to unauthenticated when no session exists', () async {
      final repo = _FakeAuthRepository()..restoredUser = null;
      final container = ProviderContainer(
        overrides: [authRepositoryProvider.overrideWithValue(repo)],
      );
      addTearDown(container.dispose);

      expect(container.read(authControllerProvider).status, AuthStatus.initializing);
      await container.read(authControllerProvider.notifier).initialize();

      final state = container.read(authControllerProvider);
      expect(state.status, AuthStatus.unauthenticated);
      expect(state.user, isNull);
    });

    test('startup transitions to authenticated when valid session is restored', () async {
      final user = sampleUser();
      final repo = _FakeAuthRepository()..restoredUser = user;
      final container = ProviderContainer(
        overrides: [authRepositoryProvider.overrideWithValue(repo)],
      );
      addTearDown(container.dispose);

      await container.read(authControllerProvider.notifier).initialize();

      final state = container.read(authControllerProvider);
      expect(state.status, AuthStatus.authenticated);
      expect(state.user, user);
    });

    test('startup enters error state without wiping session when backend is unavailable', () async {
      final repo = _FakeAuthRepository()
        ..restoreError = const AppFailure(
          type: AppFailureType.network,
          message: 'Backend unreachable',
        );
      final container = ProviderContainer(
        overrides: [authRepositoryProvider.overrideWithValue(repo)],
      );
      addTearDown(container.dispose);

      await container.read(authControllerProvider.notifier).initialize();

      final state = container.read(authControllerProvider);
      expect(state.status, AuthStatus.error);
      expect(state.errorMessage, 'Backend unreachable');
      expect(repo.clearedLocal, isFalse);
    });

    test('register stays unauthenticated and prefills email for login', () async {
      final repo = _FakeAuthRepository();
      final container = ProviderContainer(
        overrides: [authRepositoryProvider.overrideWithValue(repo)],
      );
      addTearDown(container.dispose);

      final created = await container.read(authControllerProvider.notifier).register(
            fullName: 'Grace Hopper',
            userName: 'grace.hopper',
            email: 'grace@example.com',
            password: 'StrongPassword123!',
          );

      expect(created.email, 'grace@example.com');
      final state = container.read(authControllerProvider);
      expect(state.status, AuthStatus.unauthenticated);
      expect(state.prefilledEmail, 'grace@example.com');
      expect(state.infoMessage, contains('Sign in'));
    });

    test('logout clears state and reports honest warning when server revocation fails', () async {
      final repo = _FakeAuthRepository()
        ..logoutResult = const LogoutResult(
          serverRevoked: false,
          warningMessage:
              'Signed out on this device. Could not reach the server to revoke the remote session.',
        );
      final container = ProviderContainer(
        overrides: [authRepositoryProvider.overrideWithValue(repo)],
      );
      addTearDown(container.dispose);

      await container.read(authControllerProvider.notifier).login(
            email: 'ada@example.com',
            password: 'StrongPassword123!',
          );
      expect(container.read(authControllerProvider).isAuthenticated, isTrue);

      final result = await container.read(authControllerProvider.notifier).logout();
      expect(result.serverRevoked, isFalse);

      final state = container.read(authControllerProvider);
      expect(state.status, AuthStatus.unauthenticated);
      expect(state.infoMessage, contains('Could not reach the server'));
    });
  });

  group('resolveAuthRedirect route guards', () {
    test('keeps startup phase on splash', () {
      const init = AuthState.initializing();
      expect(
        resolveAuthRedirect(authState: init, currentLocation: AppRoutes.splash),
        isNull,
      );
      expect(
        resolveAuthRedirect(authState: init, currentLocation: AppRoutes.home),
        AppRoutes.splash,
      );
    });

    test('blocks unauthenticated users from protected routes and allows login/register', () {
      const unauth = AuthState.unauthenticated();
      expect(
        resolveAuthRedirect(authState: unauth, currentLocation: AppRoutes.home),
        AppRoutes.login,
      );
      expect(
        resolveAuthRedirect(authState: unauth, currentLocation: AppRoutes.profile),
        AppRoutes.login,
      );
      expect(
        resolveAuthRedirect(authState: unauth, currentLocation: AppRoutes.login),
        isNull,
      );
      expect(
        resolveAuthRedirect(authState: unauth, currentLocation: AppRoutes.register),
        isNull,
      );
    });

    test('redirects authenticated users away from login, register, and splash to home', () {
      final auth = AuthState.authenticated(sampleUser());
      expect(
        resolveAuthRedirect(authState: auth, currentLocation: AppRoutes.login),
        AppRoutes.home,
      );
      expect(
        resolveAuthRedirect(authState: auth, currentLocation: AppRoutes.register),
        AppRoutes.home,
      );
      expect(
        resolveAuthRedirect(authState: auth, currentLocation: AppRoutes.splash),
        AppRoutes.home,
      );
      expect(
        resolveAuthRedirect(authState: auth, currentLocation: AppRoutes.home),
        isNull,
      );
      expect(
        resolveAuthRedirect(authState: auth, currentLocation: AppRoutes.profile),
        isNull,
      );
    });
  });
}
