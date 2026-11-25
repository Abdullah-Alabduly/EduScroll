import 'package:flutter/foundation.dart';

@immutable
class AuthTokens {
  const AuthTokens({
    required this.accessToken,
    required this.tokenType,
    required this.expiresAtUtc,
    required this.refreshToken,
    required this.refreshTokenExpiresAtUtc,
  });

  final String accessToken;
  final String tokenType;
  final DateTime expiresAtUtc;
  final String refreshToken;
  final DateTime refreshTokenExpiresAtUtc;

  bool isAccessTokenExpired({
    DateTime? now,
    Duration buffer = const Duration(seconds: 30),
  }) {
    final currentUtc = (now ?? DateTime.now()).toUtc();
    return !expiresAtUtc.toUtc().isAfter(currentUtc.add(buffer));
  }

  bool isRefreshTokenExpired({
    DateTime? now,
    Duration buffer = Duration.zero,
  }) {
    final currentUtc = (now ?? DateTime.now()).toUtc();
    return !refreshTokenExpiresAtUtc.toUtc().isAfter(currentUtc.add(buffer));
  }

  Map<String, dynamic> toStorageJson() => {
        'accessToken': accessToken,
        'tokenType': tokenType,
        'expiresAtUtc': expiresAtUtc.toUtc().toIso8601String(),
        'refreshToken': refreshToken,
        'refreshTokenExpiresAtUtc':
            refreshTokenExpiresAtUtc.toUtc().toIso8601String(),
      };

  factory AuthTokens.fromStorageJson(Map<String, dynamic> json) {
    final accessToken = json['accessToken']?.toString() ?? '';
    final tokenType = json['tokenType']?.toString() ?? 'Bearer';
    final refreshToken = json['refreshToken']?.toString() ?? '';
    final expiresRaw = json['expiresAtUtc']?.toString() ?? '';
    final refreshExpiresRaw =
        json['refreshTokenExpiresAtUtc']?.toString() ?? '';

    if (accessToken.isEmpty ||
        refreshToken.isEmpty ||
        expiresRaw.isEmpty ||
        refreshExpiresRaw.isEmpty) {
      throw const FormatException('Stored auth tokens are incomplete.');
    }

    return AuthTokens(
      accessToken: accessToken,
      tokenType: tokenType.isEmpty ? 'Bearer' : tokenType,
      expiresAtUtc: DateTime.parse(expiresRaw).toUtc(),
      refreshToken: refreshToken,
      refreshTokenExpiresAtUtc: DateTime.parse(refreshExpiresRaw).toUtc(),
    );
  }

  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      other is AuthTokens &&
          accessToken == other.accessToken &&
          tokenType == other.tokenType &&
          expiresAtUtc == other.expiresAtUtc &&
          refreshToken == other.refreshToken &&
          refreshTokenExpiresAtUtc == other.refreshTokenExpiresAtUtc;

  @override
  int get hashCode => Object.hash(
        accessToken,
        tokenType,
        expiresAtUtc,
        refreshToken,
        refreshTokenExpiresAtUtc,
      );

  /// Intentionally redacts sensitive token values so they cannot leak into logs.
  @override
  String toString() =>
      'AuthTokens(tokenType: $tokenType, expiresAtUtc: $expiresAtUtc, '
      'refreshTokenExpiresAtUtc: $refreshTokenExpiresAtUtc, tokens: [REDACTED])';
}
