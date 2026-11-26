import 'dart:typed_data';

import '../../../../core/error/app_failure.dart';

/// Client-side validation and text normalization aligned with EduScroll backend limits.
abstract final class SourceValidation {
  static const int maxPdfFileSizeBytes = 15 * 1024 * 1024; // 15 MB
  static const int minExtractedTextLength = 50;
  static const int maxExtractedTextLength = 200000;
  static const int maxTitleLength = 250;
  static const int maxUrlLength = 2048;

  static const List<int> _pdfHeaderSignature = [0x25, 0x50, 0x44, 0x46, 0x2D]; // %PDF-

  static AppFailure? validatePickedPdfMetadata({
    required String fileName,
    required String? extension,
    required int? sizeBytes,
  }) {
    final trimmedName = fileName.trim();
    final ext = (extension ?? _extractExtension(trimmedName))?.toLowerCase();
    if (ext != 'pdf') {
      return const AppFailure(
        type: AppFailureType.validation,
        message: 'Only PDF documents (.pdf) are supported.',
        code: 'PDF_UNSUPPORTED_FILE',
      );
    }

    if (sizeBytes != null) {
      if (sizeBytes <= 0) {
        return const AppFailure(
          type: AppFailureType.validation,
          message: 'The selected PDF file is empty.',
          code: 'PDF_CORRUPT',
        );
      }

      if (sizeBytes > maxPdfFileSizeBytes) {
        return const AppFailure(
          type: AppFailureType.payloadTooLarge,
          message:
              'This PDF is larger than the ${maxPdfFileSizeBytes ~/ (1024 * 1024)} MB limit. Please choose a smaller document.',
          code: 'PDF_FILE_TOO_LARGE',
        );
      }
    }

    return null;
  }

  /// Verifies the PDF header signature (`%PDF-`) within the first 1024 bytes per PDF spec.
  static bool hasValidPdfSignature(Uint8List bytes) {
    if (bytes.length < _pdfHeaderSignature.length) {
      return false;
    }

    final searchLimit = bytes.length < 1024 ? bytes.length : 1024;
    final maxStart = searchLimit - _pdfHeaderSignature.length;
    for (var start = 0; start <= maxStart; start++) {
      var matched = true;
      for (var i = 0; i < _pdfHeaderSignature.length; i++) {
        if (bytes[start + i] != _pdfHeaderSignature[i]) {
          matched = false;
          break;
        }
      }
      if (matched) return true;
    }
    return false;
  }

  /// Normalizes extracted PDF text while preserving meaningful paragraph boundaries.
  static String normalizeExtractedText(String rawText) {
    if (rawText.trim().isEmpty) {
      return '';
    }

    final buffer = StringBuffer();
    for (var i = 0; i < rawText.length; i++) {
      final codeUnit = rawText.codeUnitAt(i);
      if (codeUnit == 0x0D) {
        if (i + 1 < rawText.length && rawText.codeUnitAt(i + 1) == 0x0A) {
          i++;
        }
        buffer.write('\n');
        continue;
      }
      if (codeUnit == 0x2028 || codeUnit == 0x2029) {
        buffer.write('\n');
        continue;
      }
      if (codeUnit == 0x09 || codeUnit == 0x00A0) {
        buffer.write(' ');
        continue;
      }
      if (codeUnit == 0x0A) {
        buffer.write('\n');
        continue;
      }
      // Strip C0 control chars and NUL bytes
      if (codeUnit < 0x20 || codeUnit == 0x7F) {
        continue;
      }
      buffer.writeCharCode(codeUnit);
    }

    final lines = buffer.toString().split('\n');
    final paragraphs = <String>[];
    final currentParagraph = <String>[];

    for (final rawLine in lines) {
      final collapsed = _collapseInlineSpaces(rawLine);
      if (collapsed.isEmpty) {
        if (currentParagraph.isNotEmpty) {
          paragraphs.add(currentParagraph.join('\n'));
          currentParagraph.clear();
        }
        continue;
      }
      currentParagraph.add(collapsed);
    }

    if (currentParagraph.isNotEmpty) {
      paragraphs.add(currentParagraph.join('\n'));
    }

    return paragraphs.join('\n\n');
  }

  /// Validates normalized extracted text without silently truncating.
  static AppFailure? validateExtractedText(String normalizedText) {
    if (normalizedText.trim().isEmpty) {
      return const AppFailure(
        type: AppFailureType.validation,
        message:
            'No selectable text was found in this PDF. Scanned or image-only PDFs are not supported yet (OCR is not available).',
        code: 'PDF_NO_EXTRACTABLE_TEXT',
      );
    }

    if (normalizedText.length < minExtractedTextLength) {
      return const AppFailure(
        type: AppFailureType.validation,
        message:
            'This PDF contains too little readable text (at least $minExtractedTextLength characters are required).',
        code: 'PDF_TEXT_TOO_SHORT',
      );
    }

    if (normalizedText.length > maxExtractedTextLength) {
      return AppFailure(
        type: AppFailureType.payloadTooLarge,
        message:
            'Extracted text is ${normalizedText.length} characters, which exceeds the $maxExtractedTextLength-character limit. Please import a shorter document or excerpt.',
        code: 'PDF_TEXT_TOO_LONG',
      );
    }

    return null;
  }

  static String resolveTitle({
    required String? metadataTitle,
    required String originalFileName,
  }) {
    final cleanedMeta = _collapseInlineSpaces(
      (metadataTitle ?? '').replaceAll(RegExp(r'[\r\n\t]+'), ' '),
    );
    if (cleanedMeta.isNotEmpty) {
      return _clampTitle(cleanedMeta);
    }

    final base = originalFileName.trim().replaceAll('\\', '/').split('/').last;
    final withoutExt = base.toLowerCase().endsWith('.pdf')
        ? base.substring(0, base.length - 4)
        : base;
    final humanized = _collapseInlineSpaces(
      withoutExt.replaceAll(RegExp(r'[_]+'), ' '),
    );

    if (humanized.isNotEmpty) {
      return _clampTitle(humanized);
    }

    return 'Untitled PDF';
  }

  /// Validates an article URL before sending it to the backend.
  /// Returns a human-readable validation error or `null` when valid.
  static String? validateArticleUrl(String? rawValue) {
    final value = rawValue?.trim() ?? '';
    if (value.isEmpty) {
      return 'Enter an article URL.';
    }
    if (value.length > maxUrlLength) {
      return 'URL must not exceed $maxUrlLength characters.';
    }

    final uri = Uri.tryParse(value);
    if (uri == null || !uri.hasScheme) {
      return 'Enter a complete link starting with https:// or http://.';
    }

    final scheme = uri.scheme.toLowerCase();
    if (scheme != 'http' && scheme != 'https') {
      return 'Only http:// and https:// links are supported.';
    }

    final host = uri.host.trim().toLowerCase();
    if (host.isEmpty || !host.contains('.') && host != 'localhost') {
      return 'Enter a valid public website address.';
    }

    if (uri.userInfo.isNotEmpty) {
      return 'Links containing embedded credentials are not supported.';
    }

    if (host == 'localhost' ||
        host.endsWith('.localhost') ||
        host.endsWith('.local') ||
        host.endsWith('.internal') ||
        host == '127.0.0.1' ||
        host == '::1' ||
        host == '0.0.0.0') {
      return 'Local or private network addresses cannot be imported.';
    }

    return null;
  }

  static String? _extractExtension(String fileName) {
    final dot = fileName.lastIndexOf('.');
    if (dot <= 0 || dot == fileName.length - 1) return null;
    return fileName.substring(dot + 1);
  }

  static String _collapseInlineSpaces(String input) {
    return input.replaceAll(RegExp(r'[ \t\u00A0]+'), ' ').trim();
  }

  static String _clampTitle(String title) {
    if (title.length <= maxTitleLength) return title;
    return title.substring(0, maxTitleLength).trimRight();
  }
}
