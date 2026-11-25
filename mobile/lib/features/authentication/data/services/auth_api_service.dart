import 'package:dio/dio.dart';

import '../../../../core/constants/api_endpoints.dart';
import '../../../../core/error/error_mapper.dart';
import '../../../../core/network/auth_interceptor.dart';
import '../../domain/entities/auth_session.dart';
import '../../domain/entities/user_profile.dart';
import '../models/auth_response_model.dart';
import '../models/login_request_model.dart';
import '../models/register_request_model.dart';
import '../models/user_profile_model.dart';

class AuthApiService {
  AuthApiService(this._dio);

  final Dio _dio;

  Future<UserProfile> register(
    RegisterRequestModel request, {
    CancelToken? cancelToken,
  }) async {
    try {
      final response = await _dio.post<dynamic>(
        ApiEndpoints.register,
        data: request.toJson(),
        cancelToken: cancelToken,
        options: Options(extra: const {AuthInterceptor.skipAuthKey: true}),
      );
      return UserProfileModel.fromJson(_requireJsonMap(response.data));
    } catch (error) {
      throw ErrorMapper.fromError(error);
    }
  }

  Future<AuthSession> login(
    LoginRequestModel request, {
    CancelToken? cancelToken,
  }) async {
    try {
      final response = await _dio.post<dynamic>(
        ApiEndpoints.login,
        data: request.toJson(),
        cancelToken: cancelToken,
        options: Options(extra: const {AuthInterceptor.skipAuthKey: true}),
      );
      return AuthResponseModel.fromJson(_requireJsonMap(response.data));
    } catch (error) {
      throw ErrorMapper.fromError(error);
    }
  }

  Future<AuthSession> refresh(
    String refreshToken, {
    CancelToken? cancelToken,
  }) async {
    try {
      final response = await _dio.post<dynamic>(
        ApiEndpoints.refresh,
        data: <String, dynamic>{'refreshToken': refreshToken},
        cancelToken: cancelToken,
        options: Options(extra: const {AuthInterceptor.skipAuthKey: true}),
      );
      return AuthResponseModel.fromJson(_requireJsonMap(response.data));
    } catch (error) {
      throw ErrorMapper.fromError(error);
    }
  }

  Future<void> logout(
    String refreshToken, {
    CancelToken? cancelToken,
  }) async {
    try {
      await _dio.post<dynamic>(
        ApiEndpoints.logout,
        data: <String, dynamic>{'refreshToken': refreshToken},
        cancelToken: cancelToken,
      );
    } catch (error) {
      throw ErrorMapper.fromError(error);
    }
  }

  Future<UserProfile> getCurrentUser({CancelToken? cancelToken}) async {
    try {
      final response = await _dio.get<dynamic>(
        ApiEndpoints.me,
        cancelToken: cancelToken,
      );
      return UserProfileModel.fromJson(_requireJsonMap(response.data));
    } catch (error) {
      throw ErrorMapper.fromError(error);
    }
  }

  Map<String, dynamic> _requireJsonMap(dynamic data) {
    if (data is Map<String, dynamic>) {
      return data;
    }
    if (data is Map) {
      return data.map((key, value) => MapEntry(key.toString(), value));
    }
    throw const FormatException('Expected a JSON object response.');
  }
}
