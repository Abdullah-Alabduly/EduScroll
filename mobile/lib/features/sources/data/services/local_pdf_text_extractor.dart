import 'package:flutter/foundation.dart';
import 'package:syncfusion_flutter_pdf/pdf.dart';

import '../../../../core/error/app_failure.dart';
import '../../domain/entities/extracted_pdf_content.dart';
import '../../domain/entities/picked_pdf_file.dart';
import '../../domain/services/pdf_text_extractor.dart';
import 'source_validation.dart';

/// Pure-Dart local PDF text extractor backed by `syncfusion_flutter_pdf`.
/// Runs PDF parsing and text extraction inside a background isolate via [compute]
/// so the main Flutter UI thread stays responsive.
class LocalPdfTextExtractor implements PdfTextExtractorService {
  const LocalPdfTextExtractor();

  @override
  Future<ExtractedPdfContent> extract(PickedPdfFile file) async {
    final metadataFailure = SourceValidation.validatePickedPdfMetadata(
      fileName: file.name,
      extension: file.extension,
      sizeBytes: file.sizeBytes,
    );
    if (metadataFailure != null) {
      throw metadataFailure;
    }

    final Uint8List bytes;
    try {
      bytes = await file.readBytes(SourceValidation.maxPdfFileSizeBytes);
    } on AppFailure {
      rethrow;
    } catch (_) {
      throw const AppFailure(
        type: AppFailureType.validation,
        message: 'Could not read the selected PDF file. Please try again.',
        code: 'PDF_READ_FAILED',
      );
    }

    if (bytes.isEmpty) {
      throw const AppFailure(
        type: AppFailureType.validation,
        message: 'The selected PDF file is empty.',
        code: 'PDF_CORRUPT',
      );
    }

    if (bytes.length > SourceValidation.maxPdfFileSizeBytes) {
      throw const AppFailure(
        type: AppFailureType.payloadTooLarge,
        message:
            'This PDF is larger than the 15 MB limit. Please choose a smaller document.',
        code: 'PDF_FILE_TOO_LARGE',
      );
    }

    if (!SourceValidation.hasValidPdfSignature(bytes)) {
      throw const AppFailure(
        type: AppFailureType.validation,
        message:
            'This file does not appear to be a valid PDF document (invalid file header).',
        code: 'PDF_CORRUPT',
      );
    }

    final isolateResult = await compute<_PdfExtractInput, _PdfExtractOutput>(
      _extractPdfInBackground,
      _PdfExtractInput(bytes: bytes, fileName: file.name),
    );

    if (isolateResult.errorCode != null) {
      throw AppFailure(
        type: isolateResult.errorCode == 'PDF_TEXT_TOO_LONG'
            ? AppFailureType.payloadTooLarge
            : AppFailureType.validation,
        message: isolateResult.errorMessage ??
            'Failed to extract text from this PDF.',
        code: isolateResult.errorCode,
      );
    }

    final normalizedText = isolateResult.normalizedText!;
    final textFailure = SourceValidation.validateExtractedText(normalizedText);
    if (textFailure != null) {
      throw textFailure;
    }

    return ExtractedPdfContent(
      title: isolateResult.title!,
      extractedText: normalizedText,
      pageCount: isolateResult.pageCount,
      originalFileName: file.name.trim(),
    );
  }
}

class _PdfExtractInput {
  const _PdfExtractInput({
    required this.bytes,
    required this.fileName,
  });

  final Uint8List bytes;
  final String fileName;
}

class _PdfExtractOutput {
  const _PdfExtractOutput.success({
    required this.title,
    required this.normalizedText,
    required this.pageCount,
  })  : errorCode = null,
        errorMessage = null;

  const _PdfExtractOutput.failure({
    required this.errorCode,
    required this.errorMessage,
  })  : title = null,
        normalizedText = null,
        pageCount = 0;

  final String? title;
  final String? normalizedText;
  final int pageCount;
  final String? errorCode;
  final String? errorMessage;
}

_PdfExtractOutput _extractPdfInBackground(_PdfExtractInput input) {
  PdfDocument? document;
  try {
    document = PdfDocument(inputBytes: input.bytes);
    final pageCount = document.pages.count;

    if (pageCount == 0) {
      return const _PdfExtractOutput.failure(
        errorCode: 'PDF_NO_EXTRACTABLE_TEXT',
        errorMessage: 'This PDF has no pages to extract text from.',
      );
    }

    final extractor = PdfTextExtractor(document);
    final pageTexts = <String>[];
    for (var i = 0; i < pageCount; i++) {
      final pageText = extractor.extractText(
        startPageIndex: i,
        endPageIndex: i,
      );
      final trimmed = pageText.trim();
      if (trimmed.isNotEmpty) {
        pageTexts.add(trimmed);
      }
    }

    final combined = pageTexts.join('\n\n');
    final normalized = SourceValidation.normalizeExtractedText(combined);

    if (normalized.isEmpty) {
      return const _PdfExtractOutput.failure(
        errorCode: 'PDF_NO_EXTRACTABLE_TEXT',
        errorMessage:
            'No selectable text was found in this PDF. Scanned or image-only PDFs are not supported yet (OCR is not available).',
      );
    }

    final resolvedTitle = SourceValidation.resolveTitle(
      metadataTitle: document.documentInformation.title,
      originalFileName: input.fileName,
    );

    return _PdfExtractOutput.success(
      title: resolvedTitle,
      normalizedText: normalized,
      pageCount: pageCount,
    );
  } catch (error) {
    final msg = error.toString().toLowerCase();
    if (msg.contains('password') ||
        msg.contains('encrypt') ||
        msg.contains('security') ||
        msg.contains('permission')) {
      return const _PdfExtractOutput.failure(
        errorCode: 'PDF_PASSWORD_PROTECTED',
        errorMessage:
            'This PDF is password-protected. Please remove the password and try again.',
      );
    }

    return const _PdfExtractOutput.failure(
      errorCode: 'PDF_CORRUPT',
      errorMessage:
          'This PDF file appears to be corrupted or uses an unsupported format.',
    );
  } finally {
    document?.dispose();
  }
}
