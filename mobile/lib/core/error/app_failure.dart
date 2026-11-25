import 'package:flutter/foundation.dart';

enum AppFailureType {
  network,
  timeout,
  backendUnavailable,
  validation,
  unauthorized,
  forbidden,
  conflict,
  rateLimited,
  server,
  invalidResponse,
  cancelled,
  unknown,
}

@immutable
class AppFailure implements Exception {
  const AppFailure({
    required this.type,
    required this.message,
    this.code,
    this.statusCode,
    this.fieldErrors = const {},
    this.correlationId,
  });

  final AppFailureType type;
  final String message;
  final String? code;
  final int? statusCode;
  final Map<String, List<String>> fieldErrors;
  final String? correlationId;

  bool get isNetworkOrUnavailable =>
      type == AppFailureType.network ||
      type == AppFailureType.timeout ||
      type == AppFailureType.backendUnavailable;

  bool get isAuthFailure =>
      type == AppFailureType.unauthorized || type == AppFailureType.forbidden;

  String? firstFieldError(String fieldName) {
    final normalized = fieldName.toLowerCase();
    for (final entry in fieldErrors.entries) {
      if (entry.key.toLowerCase() == normalized && entry.value.isNotEmpty) {
        return entry.value.first;
      }
    }
    return null;
  }

  @override
  String toString() =>
      'AppFailure(type: $type, code: $code, statusCode: $statusCode, message: $message)';
}
