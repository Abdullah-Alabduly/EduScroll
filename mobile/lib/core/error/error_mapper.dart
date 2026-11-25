import 'dart:convert';
import 'dart:io';

import 'package:dio/dio.dart';

import 'app_failure.dart';

/// Maps network, HTTP, and RFC 9457 ProblemDetails responses into clean,
/// localization-ready [AppFailure] objects without exposing stack traces or secrets.
abstract final class ErrorMapper {
  static AppFailure fromError(Object error) {
    if (error is AppFailure) {
      return error;
    }

    if (error is DioException) {
      return _fromDioException(error);
    }

    if (error is FormatException || error is TypeError) {
      return const AppFailure(
        type: AppFailureType.invalidResponse,
        message: 'Received an unexpected response from the server. Please try again.',
        code: 'INVALID_RESPONSE',
      );
    }

    return const AppFailure(
      type: AppFailureType.unknown,
      message: 'Something went wrong. Please try again.',
      code: 'UNKNOWN_ERROR',
    );
  }

  static AppFailure _fromDioException(DioException exception) {
    final innerError = exception.error;
    if (innerError is AppFailure) {
      return innerError;
    }

    switch (exception.type) {
      case DioExceptionType.connectionTimeout:
      case DioExceptionType.sendTimeout:
      case DioExceptionType.receiveTimeout:
      case DioExceptionType.transformTimeout:
        return const AppFailure(
          type: AppFailureType.timeout,
          message:
              'The request timed out. Please check your connection and try again.',
          code: 'REQUEST_TIMEOUT',
        );

      case DioExceptionType.cancel:
        return const AppFailure(
          type: AppFailureType.cancelled,
          message: 'The request was cancelled.',
          code: 'REQUEST_CANCELLED',
        );

      case DioExceptionType.badCertificate:
        return const AppFailure(
          type: AppFailureType.network,
          message:
              'Could not verify the server security certificate. Please check your connection.',
          code: 'TLS_CERTIFICATE_ERROR',
        );

      case DioExceptionType.connectionError:
        return const AppFailure(
          type: AppFailureType.network,
          message:
              'Unable to connect to EduScroll. Check your internet connection or verify that the backend server is running.',
          code: 'NETWORK_UNREACHABLE',
        );

      case DioExceptionType.badResponse:
        return _fromResponse(exception.response);

      case DioExceptionType.unknown:
        if (exception.error is SocketException) {
          return const AppFailure(
            type: AppFailureType.network,
            message:
                'Unable to reach the server. Check your network connection and try again.',
            code: 'NETWORK_UNREACHABLE',
          );
        }
        if (exception.error is FormatException) {
          return const AppFailure(
            type: AppFailureType.invalidResponse,
            message: 'Received an invalid response from the server.',
            code: 'INVALID_RESPONSE',
          );
        }
        return const AppFailure(
          type: AppFailureType.unknown,
          message: 'A network error occurred. Please try again.',
          code: 'NETWORK_ERROR',
        );
    }
  }

  static AppFailure _fromResponse(Response<dynamic>? response) {
    if (response == null) {
      return const AppFailure(
        type: AppFailureType.backendUnavailable,
        message: 'No response was received from the server. Please try again.',
        code: 'NO_RESPONSE',
      );
    }

    final statusCode = response.statusCode ?? 0;
    final problem = _parseProblemDetails(response.data);
    final code = problem.code;
    final correlationId = problem.correlationId;

    if (statusCode == 400 || statusCode == 422) {
      final fieldErrors = problem.fieldErrors;
      final firstFieldMsg = fieldErrors.values
          .expand((list) => list)
          .cast<String?>()
          .firstWhere((m) => m != null && m.trim().isNotEmpty, orElse: () => null);

      return AppFailure(
        type: AppFailureType.validation,
        message: firstFieldMsg ??
            _safeMessage(problem.detail) ??
            'Please review your input and try again.',
        code: code ?? 'VALIDATION_FAILED',
        statusCode: statusCode,
        fieldErrors: fieldErrors,
        correlationId: correlationId,
      );
    }

    if (statusCode == 401) {
      return AppFailure(
        type: AppFailureType.unauthorized,
        message: _messageForUnauthorizedCode(code),
        code: code ?? 'UNAUTHORIZED',
        statusCode: statusCode,
        correlationId: correlationId,
      );
    }

    if (statusCode == 403) {
      return AppFailure(
        type: AppFailureType.forbidden,
        message: _messageForForbiddenCode(code),
        code: code ?? 'ACCESS_FORBIDDEN',
        statusCode: statusCode,
        correlationId: correlationId,
      );
    }

    if (statusCode == 409) {
      final fieldErrors = <String, List<String>>{};
      String message;
      switch (code) {
        case 'DUPLICATE_EMAIL':
          message = 'An account with this email already exists.';
          fieldErrors['email'] = [message];
        case 'DUPLICATE_USERNAME':
          message = 'This username is already taken. Please choose another.';
          fieldErrors['userName'] = [message];
        default:
          message = _safeMessage(problem.detail) ??
              'This resource already exists.';
      }

      return AppFailure(
        type: AppFailureType.conflict,
        message: message,
        code: code ?? 'RESOURCE_CONFLICT',
        statusCode: statusCode,
        fieldErrors: fieldErrors,
        correlationId: correlationId,
      );
    }

    if (statusCode == 429) {
      return AppFailure(
        type: AppFailureType.rateLimited,
        message: 'Too many requests. Please wait a moment and try again.',
        code: code ?? 'RATE_LIMITED',
        statusCode: statusCode,
        correlationId: correlationId,
      );
    }

    if (statusCode == 502 || statusCode == 503 || statusCode == 504) {
      return AppFailure(
        type: AppFailureType.backendUnavailable,
        message:
            'EduScroll is temporarily unavailable. Please try again shortly.',
        code: code ?? 'SERVICE_UNAVAILABLE',
        statusCode: statusCode,
        correlationId: correlationId,
      );
    }

    if (statusCode >= 500) {
      return AppFailure(
        type: AppFailureType.server,
        message: 'A server error occurred. Please try again later.',
        code: code ?? 'INTERNAL_SERVER_ERROR',
        statusCode: statusCode,
        correlationId: correlationId,
      );
    }

    return AppFailure(
      type: AppFailureType.unknown,
      message: 'Unexpected server response ($statusCode). Please try again.',
      code: code,
      statusCode: statusCode,
      correlationId: correlationId,
    );
  }

  static String _messageForUnauthorizedCode(String? code) {
    switch (code) {
      case 'INVALID_CREDENTIALS':
        return 'Invalid email or password.';
      case 'REFRESH_TOKEN_EXPIRED':
      case 'REFRESH_TOKEN_REVOKED':
      case 'REFRESH_TOKEN_REUSED':
      case 'INVALID_REFRESH_TOKEN':
        return 'Your session has expired. Please sign in again.';
      case 'USER_NOT_FOUND':
        return 'Account could not be found. Please sign in again.';
      default:
        return 'Authentication required. Please sign in to continue.';
    }
  }

  static String _messageForForbiddenCode(String? code) {
    switch (code) {
      case 'ACCOUNT_LOCKED':
        return 'Your account is temporarily locked after multiple failed attempts. Please try again later.';
      case 'ACCOUNT_INACTIVE':
        return 'This account is inactive. Please contact support for assistance.';
      default:
        return 'You do not have permission to perform this action.';
    }
  }

  static String? _safeMessage(String? raw) {
    if (raw == null) return null;
    final trimmed = raw.trim();
    if (trimmed.isEmpty || trimmed.length > 300) return null;
    if (trimmed.contains('Exception') || trimmed.contains('StackTrace')) {
      return null;
    }
    return trimmed;
  }

  static _ParsedProblemDetails _parseProblemDetails(dynamic data) {
    Map<String, dynamic>? map;
    if (data is Map<String, dynamic>) {
      map = data;
    } else if (data is Map) {
      map = data.map((key, value) => MapEntry(key.toString(), value));
    } else if (data is String && data.trim().startsWith('{')) {
      try {
        final decoded = jsonDecode(data);
        if (decoded is Map<String, dynamic>) {
          map = decoded;
        }
      } catch (_) {
        map = null;
      }
    }

    if (map == null) {
      return const _ParsedProblemDetails();
    }

    final fieldErrors = <String, List<String>>{};
    final rawErrors = map['errors'];
    if (rawErrors is Map) {
      for (final entry in rawErrors.entries) {
        final key = entry.key.toString();
        final value = entry.value;
        if (value is List) {
          final messages = value
              .map((item) => item?.toString().trim() ?? '')
              .where((item) => item.isNotEmpty)
              .toList(growable: false);
          if (messages.isNotEmpty) {
            fieldErrors[key] = messages;
          }
        } else if (value is String && value.trim().isNotEmpty) {
          fieldErrors[key] = [value.trim()];
        }
      }
    }

    return _ParsedProblemDetails(
      code: map['code']?.toString(),
      detail: map['detail']?.toString(),
      correlationId: map['correlationId']?.toString(),
      fieldErrors: fieldErrors,
    );
  }
}

class _ParsedProblemDetails {
  const _ParsedProblemDetails({
    this.code,
    this.detail,
    this.correlationId,
    this.fieldErrors = const {},
  });

  final String? code;
  final String? detail;
  final String? correlationId;
  final Map<String, List<String>> fieldErrors;
}
