import 'dart:async';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:eduscroll/app/theme/app_theme.dart';
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
import 'package:eduscroll/features/sources/presentation/providers/source_providers.dart';
import 'package:eduscroll/features/sources/presentation/screens/add_source_screen.dart';
import 'package:eduscroll/features/sources/presentation/screens/my_sources_screen.dart';
import 'package:eduscroll/features/sources/presentation/screens/pdf_import_screen.dart';
import 'package:eduscroll/features/sources/presentation/screens/source_detail_screen.dart';
import 'package:eduscroll/features/sources/presentation/screens/url_import_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';

class _WidgetFakePicker implements PdfFilePicker {
  _WidgetFakePicker(this.file);
  final PickedPdfFile? file;

  @override
  Future<PickedPdfFile?> pickPdf() async => file;
}

class _WidgetFakeExtractor implements PdfTextExtractorService {
  _WidgetFakeExtractor({this.completer, this.error});
  final Completer<ExtractedPdfContent>? completer;
  final AppFailure? error;

  @override
  Future<ExtractedPdfContent> extract(PickedPdfFile file) async {
    if (completer != null) return completer!.future;
    if (error != null) throw error!;
    return ExtractedPdfContent(
      title: 'Thermodynamics Lecture',
      extractedText:
          'Entropy increases in an isolated system according to the second law of thermodynamics.',
      pageCount: 4,
      originalFileName: file.name,
    );
  }
}

class _WidgetFakeSourceRepo implements SourceRepository {
  _WidgetFakeSourceRepo({
    List<SourceSummary>? initialItems,
    this.detailById,
    this.urlSubmitError,
    this.urlSubmitCompleter,
  }) : items = List<SourceSummary>.from(initialItems ?? const []);

  final List<SourceSummary> items;
  final Map<String, SourceDetail>? detailById;
  final AppFailure? urlSubmitError;
  final Completer<SourceDetail>? urlSubmitCompleter;
  final List<String> deletedIds = [];

  @override
  Future<SourceDetail> createPdfSource({
    required String title,
    required String extractedText,
    String? fileName,
    CancelToken? cancelToken,
  }) async {
    final now = DateTime.utc(2026, 11, 25, 20);
    final detail = SourceDetail(
      id: 'pdf-created-1',
      title: title,
      sourceType: SourceType.pdf,
      originalFileName: fileName,
      extractedText: extractedText,
      contentLength: extractedText.length,
      status: SourceStatus.completed,
      createdAtUtc: now,
      updatedAtUtc: now,
    );
    items.insert(0, detail.toSummary());
    return detail;
  }

  @override
  Future<SourceDetail> createUrlSource({
    required String url,
    CancelToken? cancelToken,
  }) async {
    if (urlSubmitCompleter != null) return urlSubmitCompleter!.future;
    if (urlSubmitError != null) throw urlSubmitError!;
    final now = DateTime.utc(2026, 11, 25, 20);
    final detail = SourceDetail(
      id: 'url-created-1',
      title: 'Clean Architecture Notes',
      sourceType: SourceType.webArticle,
      originalUrl: url,
      extractedText:
          'Dependency inversion keeps domain rules decoupled from infrastructure concerns.',
      contentLength: 80,
      status: SourceStatus.completed,
      createdAtUtc: now,
      updatedAtUtc: now,
    );
    items.insert(0, detail.toSummary());
    return detail;
  }

  @override
  Future<PagedSources> getSources({
    int page = 1,
    int pageSize = 20,
    CancelToken? cancelToken,
  }) async {
    return PagedSources(
      items: List<SourceSummary>.unmodifiable(items),
      page: page,
      pageSize: pageSize,
      totalCount: items.length,
      hasNextPage: false,
    );
  }

  @override
  Future<SourceDetail> getSourceById(String id, {CancelToken? cancelToken}) async {
    final found = detailById?[id];
    if (found != null) return found;
    throw const AppFailure(
      type: AppFailureType.notFound,
      message: 'This source could not be found.',
      code: 'SOURCE_NOT_FOUND',
    );
  }

  @override
  Future<void> deleteSource(String id, {CancelToken? cancelToken}) async {
    deletedIds.add(id);
    items.removeWhere((item) => item.id == id);
  }
}

Widget _wrapWithRouter({
  required Widget home,
  required List<Override> overrides,
  String initialLocation = '/',
  List<RouteBase>? routes,
}) {
  final router = GoRouter(
    initialLocation: initialLocation,
    routes: routes ??
        [
          GoRoute(path: '/', builder: (_, _) => home),
          GoRoute(path: '/add-source', builder: (_, _) => const AddSourceScreen()),
          GoRoute(path: '/add-source/pdf', builder: (_, _) => const PdfImportScreen()),
          GoRoute(path: '/add-source/url', builder: (_, _) => const UrlImportScreen()),
          GoRoute(
            path: '/sources/:id',
            builder: (_, state) =>
                SourceDetailScreen(sourceId: state.pathParameters['id']!),
          ),
        ],
  );

  return ProviderScope(
    overrides: overrides,
    child: MaterialApp.router(
      theme: AppTheme.dark,
      routerConfig: router,
    ),
  );
}

void main() {
  testWidgets('AddSourceScreen shows Upload PDF and Add Article Link actions', (tester) async {
    await tester.pumpWidget(
      _wrapWithRouter(
        home: const AddSourceScreen(),
        overrides: const [],
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Upload PDF'), findsOneWidget);
    expect(find.text('Add Article Link'), findsOneWidget);

    await tester.tap(find.byKey(const Key('add_source_upload_pdf_card')));
    await tester.pumpAndSettle();
    expect(find.text('Import PDF'), findsOneWidget);
  });

  testWidgets('PdfImportScreen renders selected file, extraction state, and success state', (tester) async {
    final extractCompleter = Completer<ExtractedPdfContent>();
    final picked = PickedPdfFile(
      name: 'thermo_ch2.pdf',
      extension: 'pdf',
      sizeBytes: 51200,
      readBytes: (_) async => Uint8List(0),
    );
    final repo = _WidgetFakeSourceRepo();

    await tester.pumpWidget(
      _wrapWithRouter(
        home: const PdfImportScreen(),
        overrides: [
          pdfFilePickerProvider.overrideWithValue(_WidgetFakePicker(picked)),
          pdfTextExtractorProvider.overrideWithValue(
            _WidgetFakeExtractor(completer: extractCompleter),
          ),
          sourceRepositoryProvider.overrideWithValue(repo),
        ],
      ),
    );
    await tester.pumpAndSettle();

    // Choose PDF
    await tester.tap(find.byKey(const Key('pdf_choose_file_button')));
    await tester.pumpAndSettle();

    expect(find.text('thermo_ch2.pdf'), findsOneWidget);
    expect(find.text('50.0 KB'), findsOneWidget);

    // Start extraction -> shows active extracting status
    await tester.tap(find.byKey(const Key('pdf_extract_submit_button')));
    await tester.pump();
    expect(find.byKey(const Key('pdf_processing_status')), findsOneWidget);
    expect(find.text('Extracting text locally on device...'), findsOneWidget);

    // Complete extraction -> saves & shows success card
    extractCompleter.complete(
      const ExtractedPdfContent(
        title: 'Thermodynamics Lecture',
        extractedText:
            'Entropy increases in an isolated system according to the second law of thermodynamics.',
        pageCount: 4,
        originalFileName: 'thermo_ch2.pdf',
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('pdf_success_card')), findsOneWidget);
    expect(find.text('PDF saved to your library'), findsOneWidget);
    expect(find.text('Thermodynamics Lecture'), findsOneWidget);
  });

  testWidgets('UrlImportScreen validates input, shows loading, failure, and success states', (tester) async {
    final submitCompleter = Completer<SourceDetail>();
    final repo = _WidgetFakeSourceRepo(urlSubmitCompleter: submitCompleter);

    await tester.pumpWidget(
      _wrapWithRouter(
        home: const UrlImportScreen(),
        overrides: [sourceRepositoryProvider.overrideWithValue(repo)],
      ),
    );
    await tester.pumpAndSettle();

    // Empty URL validation
    await tester.tap(find.byKey(const Key('url_submit_button')));
    await tester.pumpAndSettle();
    expect(find.text('Enter an article URL.'), findsOneWidget);

    // Localhost blocked client-side
    await tester.enterText(
      find.byKey(const Key('url_input_field')),
      'http://localhost:5221/internal',
    );
    await tester.tap(find.byKey(const Key('url_submit_button')));
    await tester.pumpAndSettle();
    expect(find.text('Local or private network addresses cannot be imported.'), findsOneWidget);

    // Valid URL triggers loading state
    await tester.enterText(
      find.byKey(const Key('url_input_field')),
      'https://example.org/clean-architecture',
    );
    await tester.tap(find.byKey(const Key('url_submit_button')));
    await tester.pump();
    expect(find.byKey(const Key('url_processing_status')), findsOneWidget);

    // Complete -> shows success card
    final now = DateTime.utc(2026, 11, 25, 20);
    submitCompleter.complete(
      SourceDetail(
        id: 'url-1',
        title: 'Clean Architecture Notes',
        sourceType: SourceType.webArticle,
        originalUrl: 'https://example.org/clean-architecture',
        extractedText: 'Dependency inversion keeps domain rules isolated.',
        contentLength: 50,
        status: SourceStatus.completed,
        createdAtUtc: now,
        updatedAtUtc: now,
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('url_success_card')), findsOneWidget);
    expect(find.text('Article saved to your library'), findsOneWidget);
  });

  testWidgets('UrlImportScreen shows failure banner while preserving URL input for retry', (tester) async {
    final repo = _WidgetFakeSourceRepo(
      urlSubmitError: const AppFailure(
        type: AppFailureType.validation,
        message: 'This address points to a private or restricted network and cannot be imported.',
        code: 'URL_SSRF_BLOCKED',
      ),
    );

    await tester.pumpWidget(
      _wrapWithRouter(
        home: const UrlImportScreen(),
        overrides: [sourceRepositoryProvider.overrideWithValue(repo)],
      ),
    );
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(const Key('url_input_field')),
      'https://rebind.example.org/test',
    );
    await tester.tap(find.byKey(const Key('url_submit_button')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('url_error_banner')), findsOneWidget);
    expect(
      find.text('This address points to a private or restricted network and cannot be imported.'),
      findsOneWidget,
    );
    // Input preserved
    expect(find.text('https://rebind.example.org/test'), findsOneWidget);
  });

  testWidgets('PdfImportScreen renders error banner cleanly when PDF extraction fails', (tester) async {
    final picked = PickedPdfFile(
      name: 'scanned.pdf',
      extension: 'pdf',
      sizeBytes: 12000,
      readBytes: (_) async => Uint8List(0),
    );
    final repo = _WidgetFakeSourceRepo();

    await tester.pumpWidget(
      _wrapWithRouter(
        home: const PdfImportScreen(),
        overrides: [
          pdfFilePickerProvider.overrideWithValue(_WidgetFakePicker(picked)),
          pdfTextExtractorProvider.overrideWithValue(
            _WidgetFakeExtractor(
              error: const AppFailure(
                type: AppFailureType.validation,
                message:
                    'No selectable text was found in this PDF. Scanned or image-only PDFs are not supported yet (OCR is not available).',
                code: 'PDF_NO_EXTRACTABLE_TEXT',
              ),
            ),
          ),
          sourceRepositoryProvider.overrideWithValue(repo),
        ],
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('pdf_choose_file_button')));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('pdf_extract_submit_button')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('pdf_error_banner')), findsOneWidget);
    expect(find.textContaining('OCR is not available'), findsOneWidget);
    expect(find.text('Retry extraction & save'), findsOneWidget);
  });

  testWidgets('MySourcesScreen shows empty state when user has no sources', (tester) async {
    final emptyRepo = _WidgetFakeSourceRepo();
    await tester.pumpWidget(
      _wrapWithRouter(
        home: const MySourcesScreen(),
        overrides: [sourceRepositoryProvider.overrideWithValue(emptyRepo)],
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('No sources saved yet'), findsOneWidget);
  });

  testWidgets('MySourcesScreen shows loaded list and deletes source after confirmation', (tester) async {
    final now = DateTime.utc(2026, 11, 25, 20);
    final loadedRepo = _WidgetFakeSourceRepo(
      initialItems: [
        SourceSummary(
          id: 'src-a',
          title: 'Algorithms Study Guide',
          sourceType: SourceType.pdf,
          originalFileName: 'algo.pdf',
          contentLength: 1450,
          textPreview: 'Big-O notation characterizes worst-case algorithmic complexity.',
          status: SourceStatus.completed,
          createdAtUtc: now,
          updatedAtUtc: now,
        ),
      ],
    );

    await tester.pumpWidget(
      _wrapWithRouter(
        home: const MySourcesScreen(),
        overrides: [sourceRepositoryProvider.overrideWithValue(loadedRepo)],
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Algorithms Study Guide'), findsOneWidget);
    expect(find.text('PDF'), findsOneWidget);

    await tester.tap(find.byKey(const Key('delete_source_src-a')));
    await tester.pumpAndSettle();
    expect(find.text('Delete source'), findsOneWidget);

    await tester.tap(find.byKey(const Key('confirm_delete_source_button')));
    await tester.pumpAndSettle();

    expect(loadedRepo.deletedIds, ['src-a']);
    expect(find.text('No sources saved yet'), findsOneWidget);
  });

  testWidgets('SourceDetailScreen renders plain text safely and supports delete confirmation', (tester) async {
    final now = DateTime.utc(2026, 11, 25, 20);
    const plainTextWithHtmlChars =
        'Literal tags like <script>alert(1)</script> must display as harmless plain text.';

    final repo = _WidgetFakeSourceRepo(
      detailById: {
        'src-42': SourceDetail(
          id: 'src-42',
          title: 'Web Security 101',
          sourceType: SourceType.webArticle,
          originalUrl: 'https://example.org/security',
          extractedText: plainTextWithHtmlChars,
          contentLength: plainTextWithHtmlChars.length,
          status: SourceStatus.completed,
          createdAtUtc: now,
          updatedAtUtc: now,
        ),
      },
    );

    await tester.pumpWidget(
      _wrapWithRouter(
        home: const SourceDetailScreen(sourceId: 'src-42'),
        overrides: [sourceRepositoryProvider.overrideWithValue(repo)],
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Web Security 101'), findsOneWidget);
    expect(find.text('https://example.org/security'), findsOneWidget);
    expect(find.text(plainTextWithHtmlChars), findsOneWidget);

    await tester.tap(find.byKey(const Key('detail_delete_button')));
    await tester.pumpAndSettle();
    expect(find.text('Delete source'), findsOneWidget);
  });
}
