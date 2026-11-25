import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../app/config/app_config.dart';
import '../../../../core/network/api_client.dart';
import '../../../../core/storage/secure_storage_service.dart';
import '../../../../core/storage/token_storage.dart';
import '../../data/repositories/auth_repository_impl.dart';
import '../../data/services/auth_api_service.dart';
import '../../data/services/session_refresher.dart';
import '../../domain/repositories/auth_repository.dart';
import '../controllers/auth_controller.dart';
import '../controllers/auth_state.dart';

final appConfigProvider = Provider<AppConfig>((ref) {
  return AppConfig.fromEnvironment();
});

final clockProvider = Provider<Clock>((ref) {
  return () => DateTime.now().toUtc();
});

final secureStorageServiceProvider = Provider<SecureStorageService>((ref) {
  return PlatformSecureStorageService();
});

final tokenStorageProvider = Provider<TokenStorage>((ref) {
  return SecureTokenStorage(ref.watch(secureStorageServiceProvider));
});

/// Holds the single shared [Dio] + [SessionRefresher] pair so there is
/// strictly one HTTP client instance across the entire application.
final apiSessionBundleProvider = Provider<ApiSessionBundle>((ref) {
  final config = ref.watch(appConfigProvider);
  final tokenStorage = ref.watch(tokenStorageProvider);
  final clock = ref.watch(clockProvider);

  late final SessionRefresher refresher;
  final dio = ApiClientFactory.create(
    config: config,
    tokenStorage: tokenStorage,
    nowProvider: clock,
    refresherFactory: (client) {
      final apiService = AuthApiService(client);
      refresher = SessionRefresher(
        tokenStorage: tokenStorage,
        refreshExecutor: (refreshToken) => apiService.refresh(refreshToken),
        nowProvider: clock,
        onSessionRefreshed: (session) {
          ref.read(authControllerProvider.notifier).handleSessionRefreshed(session);
        },
        onSessionExpired: (failure) {
          ref.read(authControllerProvider.notifier).handleSessionExpired(failure);
        },
      );
      return refresher;
    },
  );

  ref.onDispose(dio.close);
  return ApiSessionBundle(dio: dio, refresher: refresher);
});

final dioProvider = Provider<Dio>((ref) {
  return ref.watch(apiSessionBundleProvider).dio;
});

final sessionRefresherProvider = Provider<SessionRefresher>((ref) {
  return ref.watch(apiSessionBundleProvider).refresher;
});

final authApiServiceProvider = Provider<AuthApiService>((ref) {
  return AuthApiService(ref.watch(dioProvider));
});

final authRepositoryProvider = Provider<AuthRepository>((ref) {
  return AuthRepositoryImpl(
    apiService: ref.watch(authApiServiceProvider),
    tokenStorage: ref.watch(tokenStorageProvider),
    sessionRefresher: ref.watch(sessionRefresherProvider),
    nowProvider: ref.watch(clockProvider),
  );
});

final authControllerProvider = NotifierProvider<AuthController, AuthState>(
  AuthController.new,
);

class ApiSessionBundle {
  const ApiSessionBundle({
    required this.dio,
    required this.refresher,
  });

  final Dio dio;
  final SessionRefresher refresher;
}
