import '../entities/auth_session.dart';
import '../entities/user_profile.dart';

abstract interface class AuthRepository {
  Future<UserProfile> register({
    required String fullName,
    required String userName,
    required String email,
    required String password,
  });

  Future<AuthSession> login({
    required String email,
    required String password,
  });

  Future<AuthSession> refreshSession();

  Future<UserProfile> getCurrentUser();

  Future<UserProfile?> restoreSession();

  Future<LogoutResult> logout();

  Future<void> clearLocalSession();
}
