/// Backend API routes verified against EduScroll.API (Sprint 2).
abstract final class ApiEndpoints {
  static const String authPrefix = '/api/v1/auth';
  static const String register = '$authPrefix/register';
  static const String login = '$authPrefix/login';
  static const String refresh = '$authPrefix/refresh';
  static const String logout = '$authPrefix/logout';
  static const String me = '$authPrefix/me';

  static const String sources = '/api/v1/sources';
  static const String sourcesFromUrl = '$sources/from-url';
  static String sourceById(String id) => '$sources/$id';

  static const String healthLive = '/health/live';
  static const String healthReady = '/health/ready';

  static const Set<String> publicAuthPaths = {
    register,
    login,
    refresh,
  };
}
