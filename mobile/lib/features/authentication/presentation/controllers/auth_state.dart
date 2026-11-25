import 'package:flutter/foundation.dart';

import '../../domain/entities/user_profile.dart';

enum AuthStatus {
  initializing,
  unauthenticated,
  authenticating,
  authenticated,
  refreshing,
  error,
}

@immutable
class AuthState {
  const AuthState({
    required this.status,
    this.user,
    this.errorMessage,
    this.infoMessage,
    this.prefilledEmail,
  });

  const AuthState.initializing() : this(status: AuthStatus.initializing);

  const AuthState.unauthenticated({
    String? errorMessage,
    String? infoMessage,
    String? prefilledEmail,
  }) : this(
          status: AuthStatus.unauthenticated,
          errorMessage: errorMessage,
          infoMessage: infoMessage,
          prefilledEmail: prefilledEmail,
        );

  const AuthState.authenticating({String? prefilledEmail})
      : this(
          status: AuthStatus.authenticating,
          prefilledEmail: prefilledEmail,
        );

  const AuthState.authenticated(UserProfile user)
      : this(
          status: AuthStatus.authenticated,
          user: user,
        );

  const AuthState.refreshing(UserProfile user)
      : this(
          status: AuthStatus.refreshing,
          user: user,
        );

  const AuthState.startupError(String message)
      : this(
          status: AuthStatus.error,
          errorMessage: message,
        );

  final AuthStatus status;
  final UserProfile? user;
  final String? errorMessage;
  final String? infoMessage;
  final String? prefilledEmail;

  bool get isAuthenticated =>
      status == AuthStatus.authenticated || status == AuthStatus.refreshing;

  bool get isStartupPhase =>
      status == AuthStatus.initializing || status == AuthStatus.error;

  bool get isBusy =>
      status == AuthStatus.initializing ||
      status == AuthStatus.authenticating ||
      status == AuthStatus.refreshing;
}
