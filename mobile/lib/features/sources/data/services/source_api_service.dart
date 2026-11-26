import 'package:dio/dio.dart';

import '../../../../core/constants/api_endpoints.dart';
import '../../../../core/error/error_mapper.dart';
import '../../domain/entities/paged_sources.dart';
import '../../domain/entities/source_detail.dart';
import '../models/create_pdf_source_request_model.dart';
import '../models/create_url_source_request_model.dart';
import '../models/paged_sources_model.dart';
import '../models/source_detail_model.dart';

/// Communicates with EduScroll `/api/v1/sources` endpoints using the single shared [Dio] client.
class SourceApiService {
  SourceApiService(this._dio);

  final Dio _dio;

  Future<SourceDetail> createPdfSource(
    CreatePdfSourceRequestModel request, {
    CancelToken? cancelToken,
  }) async {
    try {
      final response = await _dio.post<dynamic>(
        ApiEndpoints.sources,
        data: request.toJson(),
        cancelToken: cancelToken,
      );
      return SourceDetailModel.fromJson(_requireJsonMap(response.data));
    } catch (error) {
      throw ErrorMapper.fromError(error);
    }
  }

  Future<SourceDetail> createUrlSource(
    CreateUrlSourceRequestModel request, {
    CancelToken? cancelToken,
  }) async {
    try {
      final response = await _dio.post<dynamic>(
        ApiEndpoints.sourcesFromUrl,
        data: request.toJson(),
        cancelToken: cancelToken,
        options: Options(
          // Allow enough time for secure backend web fetch + HTML parsing (backend timeout is 20s)
          receiveTimeout: const Duration(seconds: 30),
        ),
      );
      return SourceDetailModel.fromJson(_requireJsonMap(response.data));
    } catch (error) {
      throw ErrorMapper.fromError(error);
    }
  }

  Future<PagedSources> listSources({
    int page = 1,
    int pageSize = 20,
    CancelToken? cancelToken,
  }) async {
    try {
      final response = await _dio.get<dynamic>(
        ApiEndpoints.sources,
        queryParameters: <String, dynamic>{
          'page': page,
          'pageSize': pageSize,
        },
        cancelToken: cancelToken,
      );
      return PagedSourcesModel.fromJson(_requireJsonMap(response.data));
    } catch (error) {
      throw ErrorMapper.fromError(error);
    }
  }

  Future<SourceDetail> getSourceById(
    String id, {
    CancelToken? cancelToken,
  }) async {
    try {
      final response = await _dio.get<dynamic>(
        ApiEndpoints.sourceById(id),
        cancelToken: cancelToken,
      );
      return SourceDetailModel.fromJson(_requireJsonMap(response.data));
    } catch (error) {
      throw ErrorMapper.fromError(error);
    }
  }

  Future<void> deleteSource(
    String id, {
    CancelToken? cancelToken,
  }) async {
    try {
      await _dio.delete<dynamic>(
        ApiEndpoints.sourceById(id),
        cancelToken: cancelToken,
      );
    } catch (error) {
      throw ErrorMapper.fromError(error);
    }
  }

  Map<String, dynamic> _requireJsonMap(dynamic data) {
    if (data is Map<String, dynamic>) {
      return data;
    }
    if (data is Map) {
      return data.map((k, v) => MapEntry(k.toString(), v));
    }
    throw const FormatException('Expected a JSON object from the server.');
  }
}
