import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:eduscroll/core/storage/secure_storage_service.dart';
import 'package:eduscroll/features/authentication/domain/entities/auth_session.dart';
import 'package:eduscroll/features/authentication/domain/entities/auth_tokens.dart';
import 'package:eduscroll/features/authentication/domain/entities/user_profile.dart';

class InMemorySecureStorage implements SecureStorageService {
  final Map<String, String> values = <String, String>{};

  @override
  Future<String?> read({required String key}) async => values[key];

  @override
  Future<void> write({required String key, required String value}) async {
    values[key] = value;
  }

  @override
  Future<void> delete({required String key}) async {
    values.remove(key);
  }
}

typedef MockHttpHandler = Future<ResponseBody> Function(
  RequestOptions options,
  int callIndex,
);

class MockHttpClientAdapter implements HttpClientAdapter {
  MockHttpClientAdapter(this._handler);

  final MockHttpHandler _handler;
  final List<RequestOptions> requests = <RequestOptions>[];

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    requests.add(options);
    return _handler(options, requests.length);
  }

  @override
  void close({bool force = false}) {}

  static ResponseBody jsonResponse(
    Object body, {
    int statusCode = 200,
    Map<String, List<String>>? headers,
  }) {
    final encoded = utf8.encode(jsonEncode(body));
    return ResponseBody.fromBytes(
      encoded,
      statusCode,
      headers: headers ??
          <String, List<String>>{
            Headers.contentTypeHeader: <String>[
              statusCode >= 400
                  ? 'application/problem+json'
                  : 'application/json',
            ],
          },
    );
  }
}

UserProfile sampleUser({
  String id = '11111111-1111-1111-1111-111111111111',
  String fullName = 'Ada Lovelace',
  String userName = 'ada.lovelace',
  String email = 'ada@example.com',
  bool isActive = true,
}) {
  final timestamp = DateTime.utc(2026, 11, 20, 12);
  return UserProfile(
    id: id,
    fullName: fullName,
    userName: userName,
    email: email,
    createdAt: timestamp,
    updatedAt: timestamp,
    isActive: isActive,
  );
}

Map<String, dynamic> sampleUserJson({
  String id = '11111111-1111-1111-1111-111111111111',
  String fullName = 'Ada Lovelace',
  String userName = 'ada.lovelace',
  String email = 'ada@example.com',
  bool isActive = true,
}) =>
    <String, dynamic>{
      'id': id,
      'fullName': fullName,
      'userName': userName,
      'email': email,
      'createdAt': '2026-11-20T12:00:00Z',
      'updatedAt': '2026-11-20T12:00:00Z',
      'isActive': isActive,
    };

Map<String, dynamic> sampleAuthResponseJson({
  String accessToken = 'access-token-v1',
  String refreshToken = 'refresh-token-v1',
  DateTime? expiresAtUtc,
  DateTime? refreshTokenExpiresAtUtc,
  Map<String, dynamic>? user,
}) {
  final now = DateTime.utc(2026, 11, 25, 18);
  return <String, dynamic>{
    'accessToken': accessToken,
    'tokenType': 'Bearer',
    'expiresAtUtc':
        (expiresAtUtc ?? now.add(const Duration(minutes: 15))).toIso8601String(),
    'refreshToken': refreshToken,
    'refreshTokenExpiresAtUtc':
        (refreshTokenExpiresAtUtc ?? now.add(const Duration(days: 7)))
            .toIso8601String(),
    'user': user ?? sampleUserJson(),
  };
}

AuthTokens sampleTokens({
  String accessToken = 'access-token-v1',
  String refreshToken = 'refresh-token-v1',
  DateTime? expiresAtUtc,
  DateTime? refreshTokenExpiresAtUtc,
}) {
  final now = DateTime.utc(2026, 11, 25, 18);
  return AuthTokens(
    accessToken: accessToken,
    tokenType: 'Bearer',
    expiresAtUtc: expiresAtUtc ?? now.add(const Duration(minutes: 15)),
    refreshToken: refreshToken,
    refreshTokenExpiresAtUtc:
        refreshTokenExpiresAtUtc ?? now.add(const Duration(days: 7)),
  );
}

AuthSession sampleSession({
  AuthTokens? tokens,
  UserProfile? user,
}) =>
    AuthSession(
      tokens: tokens ?? sampleTokens(),
      user: user ?? sampleUser(),
    );
