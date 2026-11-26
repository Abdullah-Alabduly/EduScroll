import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../authentication/presentation/providers/auth_providers.dart';
import '../../data/repositories/source_repository_impl.dart';
import '../../data/services/local_pdf_text_extractor.dart';
import '../../data/services/platform_pdf_file_picker.dart';
import '../../data/services/source_api_service.dart';
import '../../domain/entities/source_detail.dart';
import '../../domain/repositories/source_repository.dart';
import '../../domain/services/pdf_file_picker.dart';
import '../../domain/services/pdf_text_extractor.dart';
import '../controllers/pdf_ingestion_controller.dart';
import '../controllers/pdf_ingestion_state.dart';
import '../controllers/sources_list_controller.dart';
import '../controllers/sources_list_state.dart';
import '../controllers/url_ingestion_controller.dart';
import '../controllers/url_ingestion_state.dart';

final pdfFilePickerProvider = Provider<PdfFilePicker>((ref) {
  return const PlatformPdfFilePicker();
});

final pdfTextExtractorProvider = Provider<PdfTextExtractorService>((ref) {
  return const LocalPdfTextExtractor();
});

final sourceApiServiceProvider = Provider<SourceApiService>((ref) {
  return SourceApiService(ref.watch(dioProvider));
});

final sourceRepositoryProvider = Provider<SourceRepository>((ref) {
  return SourceRepositoryImpl(
    apiService: ref.watch(sourceApiServiceProvider),
  );
});

final sourcesListControllerProvider =
    NotifierProvider<SourcesListController, SourcesListState>(
  SourcesListController.new,
);

final pdfIngestionControllerProvider =
    AutoDisposeNotifierProvider<PdfIngestionController, PdfIngestionState>(
  PdfIngestionController.new,
);

final urlIngestionControllerProvider =
    AutoDisposeNotifierProvider<UrlIngestionController, UrlIngestionState>(
  UrlIngestionController.new,
);

final sourceDetailProvider =
    FutureProvider.autoDispose.family<SourceDetail, String>((ref, id) {
  return ref.watch(sourceRepositoryProvider).getSourceById(id);
});
