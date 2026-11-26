import 'package:flutter/foundation.dart';

import '../../../../core/error/app_failure.dart';
import '../../domain/entities/extracted_pdf_content.dart';
import '../../domain/entities/picked_pdf_file.dart';
import '../../domain/entities/source_detail.dart';

enum PdfIngestionStep {
  initial,
  selectingFile,
  fileSelected,
  extracting,
  preparing,
  submitting,
  success,
  failure,
}

@immutable
class PdfIngestionState {
  const PdfIngestionState({
    required this.step,
    this.selectedFile,
    this.extractedContent,
    this.createdSource,
    this.failure,
  });

  const PdfIngestionState.initial() : this(step: PdfIngestionStep.initial);

  final PdfIngestionStep step;
  final PickedPdfFile? selectedFile;
  final ExtractedPdfContent? extractedContent;
  final SourceDetail? createdSource;
  final AppFailure? failure;

  bool get isBusy =>
      step == PdfIngestionStep.selectingFile ||
      step == PdfIngestionStep.extracting ||
      step == PdfIngestionStep.preparing ||
      step == PdfIngestionStep.submitting;

  PdfIngestionState copyWith({
    PdfIngestionStep? step,
    PickedPdfFile? selectedFile,
    ExtractedPdfContent? extractedContent,
    SourceDetail? createdSource,
    AppFailure? failure,
    bool clearSelectedFile = false,
    bool clearExtractedContent = false,
    bool clearCreatedSource = false,
    bool clearFailure = false,
  }) {
    return PdfIngestionState(
      step: step ?? this.step,
      selectedFile:
          clearSelectedFile ? null : (selectedFile ?? this.selectedFile),
      extractedContent: clearExtractedContent
          ? null
          : (extractedContent ?? this.extractedContent),
      createdSource:
          clearCreatedSource ? null : (createdSource ?? this.createdSource),
      failure: clearFailure ? null : (failure ?? this.failure),
    );
  }
}
