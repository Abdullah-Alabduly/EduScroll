import 'dart:math';

import 'package:dio/dio.dart';

/// Attaches a backend-compatible X-Correlation-Id header (32 hex chars) to each request.
class CorrelationIdInterceptor extends Interceptor {
  CorrelationIdInterceptor({Random? random}) : _random = random ?? Random.secure();

  static const String headerName = 'X-Correlation-Id';
  final Random _random;

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler handler) {
    if (!options.headers.containsKey(headerName)) {
      options.headers[headerName] = _generateCorrelationId();
    }
    handler.next(options);
  }

  String _generateCorrelationId() {
    final buffer = StringBuffer();
    for (var i = 0; i < 16; i++) {
      buffer.write(_random.nextInt(256).toRadixString(16).padLeft(2, '0'));
    }
    return buffer.toString();
  }
}
