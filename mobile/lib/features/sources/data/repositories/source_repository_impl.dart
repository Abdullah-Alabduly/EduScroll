import 'package:dio/dio.dart';

import '../../domain/entities/paged_sources.dart';
import '../../domain/entities/source_detail.dart';
import '../../domain/repositories/source_repository.dart';
import '../models/create_pdf_source_request_model.dart';
import '../models/create_url_source_request_model.dart';
import '../services/source_api_service.dart';

class SourceRepositoryImpl implements SourceRepository {
  SourceRepositoryImpl({required SourceApiService apiService})
      : _apiService = apiService;

  final SourceApiService _apiService;

  @override
  Future<SourceDetail> createPdfSource({
    required String title,
    required String extractedText,
    String? fileName,
    CancelToken? cancelToken,
  }) {
    return _apiService.createPdfSource(
      CreatePdfSourceRequestModel(
        title: title,
        extractedText: extractedText,
        fileName: fileName,
      ),
      cancelToken: cancelToken,
    );
  }

  @override
  Future<SourceDetail> createUrlSource({
    required String url,
    CancelToken? cancelToken,
  }) {
    return _apiService.createUrlSource(
      CreateUrlSourceRequestModel(url: url),
      cancelToken: cancelToken,
    );
  }

  @override
  Future<PagedSources> getSources({
    int page = 1,
    int pageSize = 20,
    CancelToken? cancelToken,
  }) {
    return _apiService.listSources(
      page: page,
      pageSize: pageSize,
      cancelToken: cancelToken,
    );
  }

  @override
  Future<SourceDetail> getSourceById(
    String id, {
    CancelToken? cancelToken,
  }) {
    return _apiService.getSourceById(id, cancelToken: cancelToken);
  }

  @override
  Future<void> deleteSource(
    String id, {
    CancelToken? cancelToken,
  }) {
    return _apiService.deleteSource(id, cancelToken: cancelToken);
  }
}
