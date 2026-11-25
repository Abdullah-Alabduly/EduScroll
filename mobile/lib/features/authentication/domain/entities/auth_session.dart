import 'package:flutter/foundation.dart';

import 'auth_tokens.dart';
import 'user_profile.dart';

@immutable
class AuthSession {
  const AuthSession({
    required this.tokens,
    required this.user,
  });

  final AuthTokens tokens;
  final UserProfile user;
}

@immutable
class LogoutResult {
  const LogoutResult({
    required this.serverRevoked,
    this.warningMessage,
  });

  final bool serverRevoked;
  final String? warningMessage;
}
