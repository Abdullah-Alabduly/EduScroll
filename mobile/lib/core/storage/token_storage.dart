import 'dart:convert';

import '../../features/authentication/domain/entities/auth_tokens.dart';
import 'secure_storage_service.dart';

abstract interface class TokenStorage {
  Future<AuthTokens?> readTokens();
  Future<void> saveTokens(AuthTokens tokens);
  Future<void> clearTokens();
}

/// Persists authentication tokens atomically in OS-encrypted secure storage.
/// Never logs or prints token contents.
class SecureTokenStorage implements TokenStorage {
  SecureTokenStorage(this._storage);

  static const String _tokensKey = 'eduscroll.auth.tokens.v1';

  final SecureStorageService _storage;

  @override
  Future<AuthTokens?> readTokens() async {
    try {
      final raw = await _storage.read(key: _tokensKey);
      if (raw == null || raw.trim().isEmpty) {
        return null;
      }

      final decoded = jsonDecode(raw);
      if (decoded is! Map<String, dynamic>) {
        await clearTokens();
        return null;
      }

      return AuthTokens.fromStorageJson(decoded);
    } catch (_) {
      // Clear corrupted/unreadable storage so startup recovers cleanly.
      try {
        await clearTokens();
      } catch (_) {}
      return null;
    }
  }

  @override
  Future<void> saveTokens(AuthTokens tokens) async {
    final encoded = jsonEncode(tokens.toStorageJson());
    await _storage.write(key: _tokensKey, value: encoded);
  }

  @override
  Future<void> clearTokens() => _storage.delete(key: _tokensKey);
}
