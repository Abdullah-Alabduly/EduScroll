import 'dart:io';
import 'dart:typed_data';
import 'dart:ui';

import 'package:eduscroll/app/config/app_config.dart';
import 'package:eduscroll/core/error/app_failure.dart';
import 'package:eduscroll/core/network/api_client.dart';
import 'package:eduscroll/core/storage/token_storage.dart';
import 'package:eduscroll/features/authentication/data/repositories/auth_repository_impl.dart';
import 'package:eduscroll/features/authentication/data/services/auth_api_service.dart';
import 'package:eduscroll/features/authentication/data/services/session_refresher.dart';
import 'package:eduscroll/features/sources/data/repositories/source_repository_impl.dart';
import 'package:eduscroll/features/sources/data/services/local_pdf_text_extractor.dart';
import 'package:eduscroll/features/sources/data/services/source_api_service.dart';
import 'package:eduscroll/features/sources/domain/entities/picked_pdf_file.dart';
import 'package:eduscroll/features/sources/domain/entities/source_type.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:syncfusion_flutter_pdf/pdf.dart';

import '../helpers/test_fakes.dart';

Future<Uint8List> _buildLiveSamplePdf() async {
  final doc = PdfDocument();
  doc.documentInformation.title = 'Live E2E Photosynthesis Guide';
  final page = doc.pages.add();
  final font = PdfStandardFont(PdfFontFamily.helvetica, 12);
  page.graphics.drawString(
    'Photosynthesis converts solar energy into chemical energy inside chloroplasts, releasing oxygen as a vital byproduct.',
    font,
    bounds: Rect.fromLTWH(
      0,
      0,
      page.getClientSize().width,
      page.getClientSize().height,
    ),
  );
  final bytes = Uint8List.fromList(await doc.save());
  doc.dispose();
  return bytes;
}

({AuthRepositoryImpl authRepo, SourceRepositoryImpl sourceRepo, void Function() dispose})
    _createClientBundle(String baseUrl) {
  final config = AppConfig(
    environment: AppEnvironment.testing,
    apiBaseUrl: baseUrl,
  );
  final tokenStorage = SecureTokenStorage(InMemorySecureStorage());
  late final SessionRefresher refresher;
  final dio = ApiClientFactory.create(
    config: config,
    tokenStorage: tokenStorage,
    refresherFactory: (client) {
      final authApi = AuthApiService(client);
      refresher = SessionRefresher(
        tokenStorage: tokenStorage,
        refreshExecutor: (rt) => authApi.refresh(rt),
      );
      return refresher;
    },
  );

  return (
    authRepo: AuthRepositoryImpl(
      apiService: AuthApiService(dio),
      tokenStorage: tokenStorage,
      sessionRefresher: refresher,
    ),
    sourceRepo: SourceRepositoryImpl(apiService: SourceApiService(dio)),
    dispose: dio.close,
  );
}

void main() {
  final liveBaseUrl = Platform.environment['EDUSCROLL_LIVE_API_URL']?.trim();

  test(
    'end-to-end Flutter PDF extraction, Sources CRUD, SSRF block, and cross-user isolation against real backend',
    () async {
      final userA = _createClientBundle(liveBaseUrl!);
      final userB = _createClientBundle(liveBaseUrl);
      addTearDown(userA.dispose);
      addTearDown(userB.dispose);

      final suffix = DateTime.now().microsecondsSinceEpoch.toString();
      const password = 'StrongPassword123!';

      // Register & login User A and User B
      await userA.authRepo.register(
        fullName: 'Source Owner A',
        userName: 'src_a_$suffix',
        email: 'src.a.$suffix@example.com',
        password: password,
      );
      await userA.authRepo.login(
        email: 'src.a.$suffix@example.com',
        password: password,
      );

      await userB.authRepo.register(
        fullName: 'Source Intruder B',
        userName: 'src_b_$suffix',
        email: 'src.b.$suffix@example.com',
        password: password,
      );
      await userB.authRepo.login(
        email: 'src.b.$suffix@example.com',
        password: password,
      );

      // 1. Extract real PDF locally off-UI-thread and submit UTF-8 text to real API
      final pdfBytes = await _buildLiveSamplePdf();
      const extractor = LocalPdfTextExtractor();
      final extracted = await extractor.extract(
        PickedPdfFile(
          name: 'photosynthesis.pdf',
          extension: 'pdf',
          sizeBytes: pdfBytes.length,
          readBytes: (_) async => pdfBytes,
        ),
      );

      final created = await userA.sourceRepo.createPdfSource(
        title: extracted.title,
        extractedText: extracted.extractedText,
        fileName: extracted.originalFileName,
      );
      expect(created.title, 'Live E2E Photosynthesis Guide');
      expect(created.sourceType, SourceType.pdf);
      expect(created.extractedText, contains('Photosynthesis converts solar energy'));

      // 2. List & detail for User A
      final listA = await userA.sourceRepo.getSources();
      expect(listA.items.any((s) => s.id == created.id), isTrue);

      final detailA = await userA.sourceRepo.getSourceById(created.id);
      expect(detailA.extractedText, created.extractedText);

      // 3. Backend SSRF protection rejects private/metadata URLs
      await expectLater(
        () => userA.sourceRepo.createUrlSource(
          url: 'http://169.254.169.254/latest/meta-data/',
        ),
        throwsA(
          isA<AppFailure>().having((f) => f.code, 'code', 'URL_SSRF_BLOCKED'),
        ),
      );

      // 4. Cross-user isolation: User B cannot list, view, or delete User A's source
      final listB = await userB.sourceRepo.getSources();
      expect(listB.items, isEmpty);

      await expectLater(
        () => userB.sourceRepo.getSourceById(created.id),
        throwsA(
          isA<AppFailure>().having((f) => f.code, 'code', 'SOURCE_NOT_FOUND'),
        ),
      );

      await expectLater(
        () => userB.sourceRepo.deleteSource(created.id),
        throwsA(
          isA<AppFailure>().having((f) => f.code, 'code', 'SOURCE_NOT_FOUND'),
        ),
      );

      // 5. User A deletes own source cleanly
      await userA.sourceRepo.deleteSource(created.id);
      await expectLater(
        () => userA.sourceRepo.getSourceById(created.id),
        throwsA(
          isA<AppFailure>().having((f) => f.code, 'code', 'SOURCE_NOT_FOUND'),
        ),
      );
    },
    skip: (liveBaseUrl == null || liveBaseUrl.isEmpty)
        ? 'Set EDUSCROLL_LIVE_API_URL to run live backend integration tests.'
        : false,
  );
}
