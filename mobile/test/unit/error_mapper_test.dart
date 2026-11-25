import 'dart:io';

import 'package:dio/dio.dart';
import 'package:eduscroll/core/error/app_failure.dart';
import 'package:eduscroll/core/error/error_mapper.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final req = RequestOptions(path: '/api/v1/auth/login');

  group('ErrorMapper', () {
    test('maps network and timeout DioExceptions', () {
      final timeout = ErrorMapper.fromError(
        DioException(
          requestOptions: req,
          type: DioExceptionType.receiveTimeout,
        ),
      );
      expect(timeout.type, AppFailureType.timeout);

      final connErr = ErrorMapper.fromError(
        DioException(
          requestOptions: req,
          type: DioExceptionType.connectionError,
        ),
      );
      expect(connErr.type, AppFailureType.network);

      final socketErr = ErrorMapper.fromError(
        DioException(
          requestOptions: req,
          type: DioExceptionType.unknown,
          error: const SocketException('Failed host lookup'),
        ),
      );
      expect(socketErr.type, AppFailureType.network);
    });

    test('parses 400 RFC 9457 ValidationProblemDetails field errors', () {
      final failure = ErrorMapper.fromError(
        DioException(
          requestOptions: req,
          type: DioExceptionType.badResponse,
          response: Response<dynamic>(
            requestOptions: req,
            statusCode: 400,
            data: <String, dynamic>{
              'status': 400,
              'code': 'VALIDATION_FAILED',
              'correlationId': 'corr-123',
              'errors': <String, dynamic>{
                'email': <String>['Email format is invalid.'],
                'password': <String>['Password must be at least 8 characters.'],
              },
            },
          ),
        ),
      );

      expect(failure.type, AppFailureType.validation);
      expect(failure.code, 'VALIDATION_FAILED');
      expect(failure.correlationId, 'corr-123');
      expect(failure.firstFieldError('email'), 'Email format is invalid.');
      expect(
        failure.firstFieldError('password'),
        'Password must be at least 8 characters.',
      );
    });

    test('maps 401, 403, 409, 429, 503, and 500 cleanly without leaking internals', () {
      AppFailure mapStatus(int status, Map<String, dynamic> data) =>
          ErrorMapper.fromError(
            DioException(
              requestOptions: req,
              type: DioExceptionType.badResponse,
              response: Response<dynamic>(
                requestOptions: req,
                statusCode: status,
                data: data,
              ),
            ),
          );

      final badCreds = mapStatus(401, {'code': 'INVALID_CREDENTIALS'});
      expect(badCreds.type, AppFailureType.unauthorized);
      expect(badCreds.message, contains('Invalid email or password'));

      final locked = mapStatus(403, {'code': 'ACCOUNT_LOCKED'});
      expect(locked.type, AppFailureType.forbidden);
      expect(locked.message, contains('locked'));

      final inactive = mapStatus(403, {'code': 'ACCOUNT_INACTIVE'});
      expect(inactive.type, AppFailureType.forbidden);
      expect(inactive.message, contains('inactive'));

      final dupEmail = mapStatus(409, {'code': 'DUPLICATE_EMAIL'});
      expect(dupEmail.type, AppFailureType.conflict);
      expect(dupEmail.firstFieldError('email'), isNotNull);

      final dupUser = mapStatus(409, {'code': 'DUPLICATE_USERNAME'});
      expect(dupUser.type, AppFailureType.conflict);
      expect(dupUser.firstFieldError('userName'), isNotNull);

      final rateLimit = mapStatus(429, {});
      expect(rateLimit.type, AppFailureType.rateLimited);

      final unavailable = mapStatus(503, {});
      expect(unavailable.type, AppFailureType.backendUnavailable);

      final serverError = mapStatus(500, {
        'detail': 'System.NullReferenceException: SQL password leaked',
      });
      expect(serverError.type, AppFailureType.server);
      expect(serverError.message, isNot(contains('NullReferenceException')));
      expect(serverError.message, isNot(contains('SQL')));
    });

    test('maps FormatException to invalidResponse', () {
      final failure = ErrorMapper.fromError(const FormatException('bad json'));
      expect(failure.type, AppFailureType.invalidResponse);
    });
  });
}
