import '../../domain/entities/auth_session.dart';
import '../../domain/entities/auth_tokens.dart';
import 'user_profile_model.dart';

abstract final class AuthResponseModel {
  static AuthSession fromJson(Map<String, dynamic> json) {
    final accessToken = _requireString(json, 'accessToken');
    final tokenType = _requireString(json, 'tokenType');
    final expiresAtRaw = _requireString(json, 'expiresAtUtc');
    final refreshToken = _requireString(json, 'refreshToken');
    final refreshExpiresRaw = _requireString(json, 'refreshTokenExpiresAtUtc');

    final userRaw = json['user'];
    if (userRaw is! Map<String, dynamic>) {
      throw const FormatException('Missing or invalid object field: user');
    }

    final tokens = AuthTokens(
      accessToken: accessToken,
      tokenType: tokenType,
      expiresAtUtc: DateTime.parse(expiresAtRaw).toUtc(),
      refreshToken: refreshToken,
      refreshTokenExpiresAtUtc: DateTime.parse(refreshExpiresRaw).toUtc(),
    );

    final user = UserProfileModel.fromJson(userRaw);
    return AuthSession(tokens: tokens, user: user);
  }

  static String _requireString(Map<String, dynamic> json, String key) {
    final value = json[key];
    if (value is! String || value.trim().isEmpty) {
      throw FormatException('Missing or invalid string field: $key');
    }
    return value;
  }
}
