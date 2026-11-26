import 'package:dio/dio.dart';

import '../entities/paged_sources.dart';
import '../entities/source_detail.dart';

abstract interface class SourceRepository {
  Future<SourceDetail> createPdfSource({
    required String title,
    required String extractedText,
    String? fileName,
    CancelToken? cancelToken,
  });

  Future<SourceDetail> createUrlSource({
    required String url,
    CancelToken? cancelToken,
  });

  Future<PagedSources> getSources({
    int page = 1,
    int pageSize = 20,
    CancelToken? cancelToken,
  });

  Future<SourceDetail> getSourceById(
    String id, {
    CancelToken? cancelToken,
  });

  Future<void> deleteSource(
    String id, {
    CancelToken? cancelToken,
  });
}
