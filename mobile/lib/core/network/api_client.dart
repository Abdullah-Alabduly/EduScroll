import 'package:dio/dio.dart';

import '../../app/config/app_config.dart';
import '../../features/authentication/data/services/session_refresher.dart';
import '../storage/token_storage.dart';
import 'auth_interceptor.dart';
import 'correlation_id_interceptor.dart';

/// Builds the single centralized [Dio] HTTP client shared across the app.
abstract final class ApiClientFactory {
  static Dio create({
    required AppConfig config,
    required TokenStorage tokenStorage,
    required SessionRefresher Function(Dio dio) refresherFactory,
    Clock? nowProvider,
    HttpClientAdapter? httpClientAdapter,
  }) {
    final dio = Dio(
      BaseOptions(
        baseUrl: config.apiBaseUrl,
        connectTimeout: config.connectTimeout,
        sendTimeout: config.sendTimeout,
        receiveTimeout: config.receiveTimeout,
        responseType: ResponseType.json,
        headers: const <String, dynamic>{
          'Accept': 'application/json',
          'Content-Type': 'application/json',
        },
      ),
    );

    if (httpClientAdapter != null) {
      dio.httpClientAdapter = httpClientAdapter;
    }

    final refresher = refresherFactory(dio);

    dio.interceptors.addAll([
      CorrelationIdInterceptor(),
      AuthInterceptor(
        dio: dio,
        tokenStorage: tokenStorage,
        sessionRefresher: refresher,
        nowProvider: nowProvider,
      ),
    ]);

    return dio;
  }
}
