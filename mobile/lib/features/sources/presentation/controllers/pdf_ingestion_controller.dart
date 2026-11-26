import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/error/app_failure.dart';
import '../../../../core/error/error_mapper.dart';
import '../../data/services/source_validation.dart';
import '../providers/source_providers.dart';
import 'pdf_ingestion_state.dart';

class PdfIngestionController extends AutoDisposeNotifier<PdfIngestionState> {
  int _operationId = 0;
  CancelToken? _activeCancelToken;

  @override
  PdfIngestionState build() {
    ref.onDispose(() {
      _operationId++;
      _activeCancelToken?.cancel('Screen disposed');
    });
    return const PdfIngestionState.initial();
  }

  Future<void> selectPdf() async {
    if (state.isBusy) return;

    final opId = ++_operationId;
    state = state.copyWith(
      step: PdfIngestionStep.selectingFile,
      clearFailure: true,
      clearCreatedSource: true,
    );

    try {
      final picked = await ref.read(pdfFilePickerProvider).pickPdf();
      if (_operationId != opId) return;

      if (picked == null) {
        state = state.selectedFile != null
            ? state.copyWith(step: PdfIngestionStep.fileSelected)
            : const PdfIngestionState.initial();
        return;
      }

      final metadataFailure = SourceValidation.validatePickedPdfMetadata(
        fileName: picked.name,
        extension: picked.extension,
        sizeBytes: picked.sizeBytes,
      );
      if (metadataFailure != null) {
        state = PdfIngestionState(
          step: PdfIngestionStep.failure,
          selectedFile: picked,
          failure: metadataFailure,
        );
        return;
      }

      state = PdfIngestionState(
        step: PdfIngestionStep.fileSelected,
        selectedFile: picked,
      );
    } catch (error) {
      if (_operationId != opId) return;
      state = state.copyWith(
        step: PdfIngestionStep.failure,
        failure: ErrorMapper.fromError(error),
      );
    }
  }

  Future<void> extractAndSubmit({String? customTitle}) async {
    if (state.isBusy) return;

    final file = state.selectedFile;
    if (file == null) {
      state = state.copyWith(
        step: PdfIngestionStep.failure,
        failure: const AppFailure(
          type: AppFailureType.validation,
          message: 'Please choose a PDF document first.',
          code: 'PDF_NO_FILE',
        ),
      );
      return;
    }

    final opId = ++_operationId;
    final cancelToken = CancelToken();
    _activeCancelToken = cancelToken;

    try {
      var extracted = state.extractedContent;

      if (extracted == null) {
        state = state.copyWith(
          step: PdfIngestionStep.extracting,
          clearFailure: true,
          clearCreatedSource: true,
        );

        extracted = await ref.read(pdfTextExtractorProvider).extract(file);
        if (_operationId != opId || cancelToken.isCancelled) return;
      }

      state = state.copyWith(
        step: PdfIngestionStep.preparing,
        extractedContent: extracted,
        clearFailure: true,
      );

      final effectiveTitle =
          (customTitle != null && customTitle.trim().isNotEmpty)
              ? customTitle.trim()
              : extracted.title;

      state = state.copyWith(
        step: PdfIngestionStep.submitting,
        extractedContent: extracted,
      );

      final created = await ref.read(sourceRepositoryProvider).createPdfSource(
            title: effectiveTitle,
            extractedText: extracted.extractedText,
            fileName: extracted.originalFileName,
            cancelToken: cancelToken,
          );

      if (_operationId != opId || cancelToken.isCancelled) return;

      ref.read(sourcesListControllerProvider.notifier).prependCreated(created);

      state = PdfIngestionState(
        step: PdfIngestionStep.success,
        selectedFile: file,
        extractedContent: extracted,
        createdSource: created,
      );
    } catch (error) {
      if (_operationId != opId) return;
      final failure = ErrorMapper.fromError(error);
      if (failure.type == AppFailureType.cancelled) {
        state = state.copyWith(
          step: PdfIngestionStep.fileSelected,
          clearFailure: true,
        );
        return;
      }

      state = state.copyWith(
        step: PdfIngestionStep.failure,
        failure: failure,
      );
    } finally {
      if (identical(_activeCancelToken, cancelToken)) {
        _activeCancelToken = null;
      }
    }
  }

  void cancelActiveOperation() {
    if (!state.isBusy) return;
    _operationId++;
    _activeCancelToken?.cancel('Cancelled by user');
    _activeCancelToken = null;

    state = state.selectedFile != null
        ? state.copyWith(
            step: PdfIngestionStep.fileSelected,
            clearFailure: true,
          )
        : const PdfIngestionState.initial();
  }

  void reset() {
    _operationId++;
    _activeCancelToken?.cancel('Reset');
    _activeCancelToken = null;
    state = const PdfIngestionState.initial();
  }
}
