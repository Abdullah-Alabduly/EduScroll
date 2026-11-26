import 'dart:async';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:eduscroll/core/error/app_failure.dart';
import 'package:eduscroll/features/sources/domain/entities/extracted_pdf_content.dart';
import 'package:eduscroll/features/sources/domain/entities/paged_sources.dart';
import 'package:eduscroll/features/sources/domain/entities/picked_pdf_file.dart';
import 'package:eduscroll/features/sources/domain/entities/source_detail.dart';
import 'package:eduscroll/features/sources/domain/entities/source_status.dart';
import 'package:eduscroll/features/sources/domain/entities/source_summary.dart';
import 'package:eduscroll/features/sources/domain/entities/source_type.dart';
import 'package:eduscroll/features/sources/domain/repositories/source_repository.dart';
import 'package:eduscroll/features/sources/domain/services/pdf_file_picker.dart';
import 'package:eduscroll/features/sources/domain/services/pdf_text_extractor.dart';
import 'package:eduscroll/features/sources/presentation/controllers/pdf_ingestion_state.dart';
import 'package:eduscroll/features/sources/presentation/controllers/sources_list_state.dart';
import 'package:eduscroll/features/sources/presentation/controllers/url_ingestion_state.dart';
import 'package:eduscroll/features/sources/presentation/providers/source_providers.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

class _FakePdfFilePicker implements PdfFilePicker {
  _FakePdfFilePicker(this.file);
  PickedPdfFile? file;

  @override
  Future<PickedPdfFile?> pickPdf() async => file;
}

class _FakePdfExtractor implements PdfTextExtractorService {
  Future<ExtractedPdfContent> Function(PickedPdfFile)? onExtract;

  @override
  Future<ExtractedPdfContent> extract(PickedPdfFile file) {
    if (onExtract != null) return onExtract!(file);
    return Future.value(
      ExtractedPdfContent(
        title: 'Extracted Title',
        extractedText:
            'Extracted textbook chapter text containing more than fifty characters for testing.',
        pageCount: 3,
        originalFileName: file.name,
      ),
    );
  }
}

class _FakeSourceRepository implements SourceRepository {
  int createPdfCalls = 0;
  int createUrlCalls = 0;
  int deleteCalls = 0;
  Future<SourceDetail> Function(String title, String text, String? fileName, CancelToken?)?
      onCreatePdf;
  Future<SourceDetail> Function(String url, CancelToken?)? onCreateUrl;
  Future<PagedSources> Function(int page, int pageSize)? onGetSources;
  Future<void> Function(String id)? onDelete;

  static SourceDetail sampleDetail({
    String id = 's-1',
    String title = 'Sample Source',
    SourceType type = SourceType.pdf,
    String? url,
    String? fileName = 'sample.pdf',
  }) {
    final now = DateTime.utc(2026, 11, 25, 19);
    return SourceDetail(
      id: id,
      title: title,
      sourceType: type,
      originalUrl: url,
      originalFileName: fileName,
      extractedText:
          'This is valid extracted educational text containing enough characters to pass validation.',
      contentLength: 90,
      status: SourceStatus.completed,
      createdAtUtc: now,
      updatedAtUtc: now,
    );
  }

  @override
  Future<SourceDetail> createPdfSource({
    required String title,
    required String extractedText,
    String? fileName,
    CancelToken? cancelToken,
  }) async {
    createPdfCalls++;
    if (onCreatePdf != null) {
      return onCreatePdf!(title, extractedText, fileName, cancelToken);
    }
    return sampleDetail(id: 'pdf-$createPdfCalls', title: title, fileName: fileName);
  }

  @override
  Future<SourceDetail> createUrlSource({
    required String url,
    CancelToken? cancelToken,
  }) async {
    createUrlCalls++;
    if (onCreateUrl != null) {
      return onCreateUrl!(url, cancelToken);
    }
    return sampleDetail(
      id: 'url-$createUrlCalls',
      title: 'Web Article',
      type: SourceType.webArticle,
      url: url,
      fileName: null,
    );
  }

  @override
  Future<PagedSources> getSources({
    int page = 1,
    int pageSize = 20,
    CancelToken? cancelToken,
  }) async {
    if (onGetSources != null) return onGetSources!(page, pageSize);
    return PagedSources(
      items: const [],
      page: page,
      pageSize: pageSize,
      totalCount: 0,
      hasNextPage: false,
    );
  }

  @override
  Future<SourceDetail> getSourceById(String id, {CancelToken? cancelToken}) async {
    return sampleDetail(id: id);
  }

  @override
  Future<void> deleteSource(String id, {CancelToken? cancelToken}) async {
    deleteCalls++;
    if (onDelete != null) return onDelete!(id);
  }
}

void main() {
  PickedPdfFile samplePickedFile() => PickedPdfFile(
        name: 'biology_notes.pdf',
        extension: 'pdf',
        sizeBytes: 24 * 1024,
        readBytes: (_) async => Uint8List(0),
      );

  group('PdfIngestionController', () {
    test('transitions through select -> extract -> submit -> success and updates SourcesList', () async {
      final repo = _FakeSourceRepository();
      final container = ProviderContainer(
        overrides: [
          pdfFilePickerProvider.overrideWithValue(_FakePdfFilePicker(samplePickedFile())),
          pdfTextExtractorProvider.overrideWithValue(_FakePdfExtractor()),
          sourceRepositoryProvider.overrideWithValue(repo),
        ],
      );
      addTearDown(container.dispose);

      final sub = container.listen(pdfIngestionControllerProvider, (_, _) {});
      addTearDown(sub.close);

      final ctrl = container.read(pdfIngestionControllerProvider.notifier);
      await ctrl.selectPdf();
      expect(container.read(pdfIngestionControllerProvider).step, PdfIngestionStep.fileSelected);

      await ctrl.extractAndSubmit();
      final finalState = container.read(pdfIngestionControllerProvider);
      expect(finalState.step, PdfIngestionStep.success);
      expect(finalState.createdSource?.title, 'Extracted Title');
      expect(repo.createPdfCalls, 1);

      // Sources list receives newly created source immediately
      final listState = container.read(sourcesListControllerProvider);
      expect(listState.items.length, 1);
      expect(listState.items.first.id, finalState.createdSource!.id);
    });

    test('prevents duplicate concurrent PDF submissions while busy', () async {
      final submitCompleter = Completer<SourceDetail>();
      final repo = _FakeSourceRepository()
        ..onCreatePdf = (_, _, _, _) => submitCompleter.future;

      final container = ProviderContainer(
        overrides: [
          pdfFilePickerProvider.overrideWithValue(_FakePdfFilePicker(samplePickedFile())),
          pdfTextExtractorProvider.overrideWithValue(_FakePdfExtractor()),
          sourceRepositoryProvider.overrideWithValue(repo),
        ],
      );
      addTearDown(container.dispose);

      final sub = container.listen(pdfIngestionControllerProvider, (_, _) {});
      addTearDown(sub.close);

      final ctrl = container.read(pdfIngestionControllerProvider.notifier);
      await ctrl.selectPdf();

      final firstCall = ctrl.extractAndSubmit();
      final duplicateCall = ctrl.extractAndSubmit();
      await duplicateCall;

      expect(repo.createPdfCalls, 1);
      submitCompleter.complete(_FakeSourceRepository.sampleDetail(id: 'pdf-once'));
      await firstCall;
      expect(container.read(pdfIngestionControllerProvider).step, PdfIngestionStep.success);
    });

    test('recovers cleanly to failure state without leaving loading active', () async {
      final extractor = _FakePdfExtractor()
        ..onExtract = (_) async => throw const AppFailure(
              type: AppFailureType.validation,
              message: 'Password protected PDF.',
              code: 'PDF_PASSWORD_PROTECTED',
            );

      final container = ProviderContainer(
        overrides: [
          pdfFilePickerProvider.overrideWithValue(_FakePdfFilePicker(samplePickedFile())),
          pdfTextExtractorProvider.overrideWithValue(extractor),
          sourceRepositoryProvider.overrideWithValue(_FakeSourceRepository()),
        ],
      );
      addTearDown(container.dispose);

      final sub = container.listen(pdfIngestionControllerProvider, (_, _) {});
      addTearDown(sub.close);

      final ctrl = container.read(pdfIngestionControllerProvider.notifier);
      await ctrl.selectPdf();
      await ctrl.extractAndSubmit();

      final state = container.read(pdfIngestionControllerProvider);
      expect(state.step, PdfIngestionStep.failure);
      expect(state.isBusy, isFalse);
      expect(state.failure?.code, 'PDF_PASSWORD_PROTECTED');
    });
  });

  group('UrlIngestionController', () {
    test('validates URL locally, preserves input on failure, and prevents duplicate submissions', () async {
      final submitCompleter = Completer<SourceDetail>();
      final repo = _FakeSourceRepository()
        ..onCreateUrl = (_, _) => submitCompleter.future;

      final container = ProviderContainer(
        overrides: [
          sourceRepositoryProvider.overrideWithValue(repo),
        ],
      );
      addTearDown(container.dispose);

      final sub = container.listen(urlIngestionControllerProvider, (_, _) {});
      addTearDown(sub.close);

      final ctrl = container.read(urlIngestionControllerProvider.notifier);

      // Malformed URL rejected without calling API
      await ctrl.submitUrl('ftp://invalid');
      expect(container.read(urlIngestionControllerProvider).step, UrlIngestionStep.failure);
      expect(container.read(urlIngestionControllerProvider).urlInput, 'ftp://invalid');
      expect(repo.createUrlCalls, 0);

      // Valid URL prevents duplicate concurrent submission
      final first = ctrl.submitUrl('https://example.org/calculus');
      final second = ctrl.submitUrl('https://example.org/calculus');
      await second;
      expect(repo.createUrlCalls, 1);

      submitCompleter.complete(
        _FakeSourceRepository.sampleDetail(
          id: 'url-1',
          title: 'Calculus Guide',
          type: SourceType.webArticle,
          url: 'https://example.org/calculus',
          fileName: null,
        ),
      );
      await first;
      expect(container.read(urlIngestionControllerProvider).step, UrlIngestionStep.success);
      expect(container.read(sourcesListControllerProvider).items.first.id, 'url-1');
    });
  });

  group('SourcesListController', () {
    test('paginates cleanly and deletes owned source from list', () async {
      final now = DateTime.utc(2026, 11, 25, 20);
      SourceSummary makeSummary(String id) => SourceSummary(
            id: id,
            title: 'Source $id',
            sourceType: SourceType.pdf,
            contentLength: 200,
            textPreview: 'Preview $id',
            status: SourceStatus.completed,
            createdAtUtc: now,
            updatedAtUtc: now,
          );

      final repo = _FakeSourceRepository()
        ..onGetSources = (page, pageSize) async {
          if (page == 1) {
            return PagedSources(
              items: [makeSummary('1'), makeSummary('2')],
              page: 1,
              pageSize: pageSize,
              totalCount: 3,
              hasNextPage: true,
            );
          }
          return PagedSources(
            items: [makeSummary('3')],
            page: 2,
            pageSize: pageSize,
            totalCount: 3,
            hasNextPage: false,
          );
        };

      final container = ProviderContainer(
        overrides: [
          sourceRepositoryProvider.overrideWithValue(repo),
        ],
      );
      addTearDown(container.dispose);

      final ctrl = container.read(sourcesListControllerProvider.notifier);
      await ctrl.loadFirstPage();
      expect(container.read(sourcesListControllerProvider).items.length, 2);
      expect(container.read(sourcesListControllerProvider).hasNextPage, isTrue);

      await ctrl.loadNextPage();
      expect(container.read(sourcesListControllerProvider).items.length, 3);
      expect(container.read(sourcesListControllerProvider).hasNextPage, isFalse);

      final failure = await ctrl.deleteSource('2');
      expect(failure, isNull);
      expect(repo.deleteCalls, 1);
      expect(container.read(sourcesListControllerProvider).items.map((e) => e.id), ['1', '3']);
      expect(container.read(sourcesListControllerProvider).totalCount, 2);
      expect(container.read(sourcesListControllerProvider).status, SourcesListStatus.loaded);
    });
  });
}
