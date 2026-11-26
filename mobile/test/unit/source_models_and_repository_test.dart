import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:eduscroll/core/constants/api_endpoints.dart';
import 'package:eduscroll/core/error/app_failure.dart';
import 'package:eduscroll/core/error/error_mapper.dart';
import 'package:eduscroll/features/sources/data/models/create_pdf_source_request_model.dart';
import 'package:eduscroll/features/sources/data/models/paged_sources_model.dart';
import 'package:eduscroll/features/sources/data/models/source_detail_model.dart';
import 'package:eduscroll/features/sources/data/repositories/source_repository_impl.dart';
import 'package:eduscroll/features/sources/data/services/source_api_service.dart';
import 'package:eduscroll/features/sources/domain/entities/source_status.dart';
import 'package:eduscroll/features/sources/domain/entities/source_type.dart';
import 'package:flutter_test/flutter_test.dart';

import '../helpers/test_fakes.dart';

Map<String, dynamic> _sampleSourceDetailJson({
  String id = 'src-1',
  String title = 'Cellular Respiration',
  String sourceType = 'Pdf',
  String? originalUrl,
  String? originalFileName = 'bio.pdf',
  String extractedText =
      'Cellular respiration converts biochemical energy from nutrients into ATP molecules.',
}) {
  return <String, dynamic>{
    'id': id,
    'title': title,
    'sourceType': sourceType,
    'originalUrl': originalUrl,
    'originalFileName': originalFileName,
    'extractedText': extractedText,
    'contentLength': extractedText.length,
    'status': 'Completed',
    'createdAtUtc': '2026-11-25T18:00:00Z',
    'updatedAtUtc': '2026-11-25T18:00:00Z',
  };
}

void main() {
  group('Source models serialization & deserialization', () {
    test('CreatePdfSourceRequestModel sends UTF-8 text + metadata only (no ownerUserId or file bytes)', () {
      const model = CreatePdfSourceRequestModel(
        title: '  Physics Notes  ',
        extractedText: 'Newton laws of motion explain force, mass, and acceleration.',
        fileName: 'physics.pdf',
      );

      final json = model.toJson();
      expect(json['title'], 'Physics Notes');
      expect(json['sourceType'], 'Pdf');
      expect(json['extractedText'], contains('Newton laws'));
      expect(json['fileName'], 'physics.pdf');
      expect(json.containsKey('ownerUserId'), isFalse);
      expect(json.containsKey('bytes'), isFalse);
    });

    test('SourceDetailModel and PagedSourcesModel parse backend DTO payloads accurately', () {
      final detail = SourceDetailModel.fromJson(_sampleSourceDetailJson());
      expect(detail.id, 'src-1');
      expect(detail.sourceType, SourceType.pdf);
      expect(detail.status, SourceStatus.completed);
      expect(detail.originalFileName, 'bio.pdf');

      final paged = PagedSourcesModel.fromJson(<String, dynamic>{
        'items': [
          {
            'id': 'src-2',
            'title': 'Neural Nets',
            'sourceType': 'WebArticle',
            'originalUrl': 'https://example.org/nn',
            'originalFileName': null,
            'contentLength': 512,
            'textPreview': 'Neural networks learn layered representations...',
            'status': 'Completed',
            'createdAtUtc': '2026-11-25T19:00:00Z',
            'updatedAtUtc': '2026-11-25T19:00:00Z',
          }
        ],
        'page': 1,
        'pageSize': 20,
        'totalCount': 1,
        'hasNextPage': false,
      });

      expect(paged.items.length, 1);
      expect(paged.items.first.sourceType, SourceType.webArticle);
      expect(paged.items.first.originalUrl, 'https://example.org/nn');
      expect(paged.hasNextPage, isFalse);
    });
  });

  group('SourceRepositoryImpl over shared Dio contract', () {
    test('maps createPdf, createUrl, list, getById, and delete endpoints accurately', () async {
      final recordedCalls = <String>[];

      final adapter = MockHttpClientAdapter((options, _) async {
        recordedCalls.add('${options.method} ${options.path}');

        if (options.method == 'POST' && options.path == ApiEndpoints.sources) {
          final body = options.data as Map<String, dynamic>;
          expect(body['sourceType'], 'Pdf');
          return MockHttpClientAdapter.jsonResponse(
            _sampleSourceDetailJson(id: 'pdf-123', title: body['title'] as String),
            statusCode: 201,
          );
        }

        if (options.method == 'POST' &&
            options.path == ApiEndpoints.sourcesFromUrl) {
          final body = options.data as Map<String, dynamic>;
          expect(body['url'], 'https://example.org/article');
          return MockHttpClientAdapter.jsonResponse(
            _sampleSourceDetailJson(
              id: 'url-456',
              title: 'Web Article Title',
              sourceType: 'WebArticle',
              originalUrl: 'https://example.org/article',
              originalFileName: null,
            ),
            statusCode: 201,
          );
        }

        if (options.method == 'GET' && options.path == ApiEndpoints.sources) {
          expect(options.queryParameters['page'], 1);
          expect(options.queryParameters['pageSize'], 10);
          return MockHttpClientAdapter.jsonResponse(<String, dynamic>{
            'items': const <Map<String, dynamic>>[],
            'page': 1,
            'pageSize': 10,
            'totalCount': 0,
            'hasNextPage': false,
          });
        }

        if (options.method == 'GET' &&
            options.path == ApiEndpoints.sourceById('pdf-123')) {
          return MockHttpClientAdapter.jsonResponse(
            _sampleSourceDetailJson(id: 'pdf-123'),
          );
        }

        if (options.method == 'DELETE' &&
            options.path == ApiEndpoints.sourceById('pdf-123')) {
          return ResponseBody.fromString('', 204);
        }

        return MockHttpClientAdapter.jsonResponse({}, statusCode: 404);
      });

      final dio = Dio(BaseOptions(baseUrl: 'http://localhost:5221'))
        ..httpClientAdapter = adapter;
      final repository = SourceRepositoryImpl(apiService: SourceApiService(dio));

      final createdPdf = await repository.createPdfSource(
        title: 'Study Notes',
        extractedText: 'Extracted text with enough length for validation checks.',
        fileName: 'notes.pdf',
      );
      expect(createdPdf.id, 'pdf-123');

      final createdUrl = await repository.createUrlSource(
        url: 'https://example.org/article',
      );
      expect(createdUrl.sourceType, SourceType.webArticle);

      final paged = await repository.getSources(page: 1, pageSize: 10);
      expect(paged.totalCount, 0);

      final fetched = await repository.getSourceById('pdf-123');
      expect(fetched.id, 'pdf-123');

      await repository.deleteSource('pdf-123');

      expect(recordedCalls, [
        'POST /api/v1/sources',
        'POST /api/v1/sources/from-url',
        'GET /api/v1/sources',
        'GET /api/v1/sources/pdf-123',
        'DELETE /api/v1/sources/pdf-123',
      ]);
    });
  });

  group('ErrorMapper — Sprint 4 Source & URL ProblemDetails mapping', () {
    DioException buildProblemError(int status, String code, String detail) {
      final request = RequestOptions(path: ApiEndpoints.sourcesFromUrl);
      return DioException(
        requestOptions: request,
        type: DioExceptionType.badResponse,
        response: Response<dynamic>(
          requestOptions: request,
          statusCode: status,
          data: jsonDecode(
            jsonEncode({'status': status, 'code': code, 'detail': detail}),
          ),
        ),
      );
    }

    test('maps SSRF, redirect, 404, 413, 422, and 504 codes cleanly', () {
      expect(
        ErrorMapper.fromError(
          buildProblemError(400, 'URL_SSRF_BLOCKED', 'blocked'),
        ).message,
        contains('private or restricted network'),
      );
      expect(
        ErrorMapper.fromError(
          buildProblemError(400, 'URL_REDIRECT_BLOCKED', 'blocked'),
        ).message,
        contains('redirected to a restricted destination'),
      );
      expect(
        ErrorMapper.fromError(
          buildProblemError(404, 'SOURCE_NOT_FOUND', 'missing'),
        ).type,
        AppFailureType.notFound,
      );
      expect(
        ErrorMapper.fromError(
          buildProblemError(413, 'URL_RESPONSE_TOO_LARGE', 'too large'),
        ).type,
        AppFailureType.payloadTooLarge,
      );
      expect(
        ErrorMapper.fromError(
          buildProblemError(422, 'URL_UNSUPPORTED_CONTENT_TYPE', 'pdf'),
        ).message,
        contains('not a readable HTML article'),
      );
      expect(
        ErrorMapper.fromError(
          buildProblemError(504, 'URL_TIMEOUT', 'timeout'),
        ).type,
        AppFailureType.timeout,
      );
    });
  });
}
