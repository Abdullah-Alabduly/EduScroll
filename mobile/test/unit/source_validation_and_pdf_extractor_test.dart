import 'dart:convert';
import 'dart:typed_data';
import 'dart:ui';

import 'package:eduscroll/core/error/app_failure.dart';
import 'package:eduscroll/features/sources/data/services/local_pdf_text_extractor.dart';
import 'package:eduscroll/features/sources/data/services/source_validation.dart';
import 'package:eduscroll/features/sources/domain/entities/picked_pdf_file.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:syncfusion_flutter_pdf/pdf.dart';

Future<Uint8List> _createSamplePdfBytes({
  String? title,
  List<String> pageTexts = const [],
  String? userPassword,
}) async {
  final document = PdfDocument();
  if (title != null) {
    document.documentInformation.title = title;
  }
  if (userPassword != null) {
    document.security.userPassword = userPassword;
    document.security.ownerPassword = '${userPassword}_owner';
    document.security.algorithm = PdfEncryptionAlgorithm.aesx128Bit;
  }

  final font = PdfStandardFont(PdfFontFamily.helvetica, 12);
  for (final text in pageTexts) {
    final page = document.pages.add();
    if (text.isNotEmpty) {
      page.graphics.drawString(
        text,
        font,
        bounds: Rect.fromLTWH(
          0,
          0,
          page.getClientSize().width,
          page.getClientSize().height,
        ),
      );
    }
  }

  final bytes = Uint8List.fromList(await document.save());
  document.dispose();
  return bytes;
}

PickedPdfFile _pickedFromBytes({
  required String name,
  required Uint8List bytes,
  String? extension = 'pdf',
  int? sizeOverride,
}) {
  return PickedPdfFile(
    name: name,
    extension: extension,
    sizeBytes: sizeOverride ?? bytes.length,
    readBytes: (_) async => bytes,
  );
}

void main() {
  group('SourceValidation — PDF metadata & text', () {
    test('rejects non-PDF extension and oversized PDFs', () {
      final badExt = SourceValidation.validatePickedPdfMetadata(
        fileName: 'notes.docx',
        extension: 'docx',
        sizeBytes: 1024,
      );
      expect(badExt?.code, 'PDF_UNSUPPORTED_FILE');

      final tooBig = SourceValidation.validatePickedPdfMetadata(
        fileName: 'huge.pdf',
        extension: 'pdf',
        sizeBytes: SourceValidation.maxPdfFileSizeBytes + 1,
      );
      expect(tooBig?.code, 'PDF_FILE_TOO_LARGE');
      expect(tooBig?.type, AppFailureType.payloadTooLarge);
    });

    test('validates %PDF- signature and rejects non-PDF binary bytes', () {
      expect(
        SourceValidation.hasValidPdfSignature(
          Uint8List.fromList(utf8.encode('%PDF-1.7\n1 0 obj')),
        ),
        isTrue,
      );
      expect(
        SourceValidation.hasValidPdfSignature(
          Uint8List.fromList(utf8.encode('not a pdf document at all')),
        ),
        isFalse,
      );
    });

    test('normalizes whitespace while preserving paragraph boundaries', () {
      const raw = '  First   paragraph\tline 1\r\nline 2\r\n\r\n\r\n  Second   paragraph  ';
      expect(
        SourceValidation.normalizeExtractedText(raw),
        'First paragraph line 1\nline 2\n\nSecond paragraph',
      );
    });

    test('rejects empty, too-short, and oversized text without silent truncation', () {
      final emptyFailure = SourceValidation.validateExtractedText('   ');
      expect(emptyFailure?.code, 'PDF_NO_EXTRACTABLE_TEXT');
      expect(emptyFailure?.message.toLowerCase(), contains('ocr'));

      final shortFailure = SourceValidation.validateExtractedText('Too short');
      expect(shortFailure?.code, 'PDF_TEXT_TOO_SHORT');

      final oversized = 'a' * (SourceValidation.maxExtractedTextLength + 5);
      final longFailure = SourceValidation.validateExtractedText(oversized);
      expect(longFailure?.code, 'PDF_TEXT_TOO_LONG');
      expect(longFailure?.type, AppFailureType.payloadTooLarge);
    });

    test('resolves title from PDF metadata or falls back cleanly to filename', () {
      expect(
        SourceValidation.resolveTitle(
          metadataTitle: '  Organic Chemistry Notes  ',
          originalFileName: 'chem_01.pdf',
        ),
        'Organic Chemistry Notes',
      );
      expect(
        SourceValidation.resolveTitle(
          metadataTitle: '',
          originalFileName: 'lecture_04_algorithms.pdf',
        ),
        'lecture 04 algorithms',
      );
    });
  });

  group('SourceValidation — Article URLs', () {
    test('rejects empty, malformed, non-HTTP, credentialed, and local URLs', () {
      expect(SourceValidation.validateArticleUrl(''), isNotNull);
      expect(SourceValidation.validateArticleUrl('not-a-url'), isNotNull);
      expect(SourceValidation.validateArticleUrl('ftp://example.org/doc'), isNotNull);
      expect(SourceValidation.validateArticleUrl('http://localhost:5221/test'), isNotNull);
      expect(SourceValidation.validateArticleUrl('http://127.0.0.1/admin'), isNotNull);
      expect(
        SourceValidation.validateArticleUrl('https://user:pass@example.org/a'),
        isNotNull,
      );
    });

    test('accepts valid public HTTP and HTTPS URLs', () {
      expect(
        SourceValidation.validateArticleUrl('https://en.wikipedia.org/wiki/Mitosis'),
        isNull,
      );
      expect(
        SourceValidation.validateArticleUrl('http://example.org/learn/physics'),
        isNull,
      );
    });
  });

  group('LocalPdfTextExtractor — real pure-Dart extraction in background isolate', () {
    const extractor = LocalPdfTextExtractor();

    test('extracts text and metadata title from a real multi-page PDF', () async {
      final bytes = await _createSamplePdfBytes(
        title: 'Operating Systems Fundamentals',
        pageTexts: [
          'A process is an executing instance of a program with its own virtual address space and system resources.',
          'Virtual memory isolates processes and lets the operating system map logical addresses to physical RAM frames.',
        ],
      );

      final result = await extractor.extract(
        _pickedFromBytes(name: 'os_notes.pdf', bytes: bytes),
      );

      expect(result.title, 'Operating Systems Fundamentals');
      expect(result.pageCount, 2);
      expect(result.originalFileName, 'os_notes.pdf');
      expect(result.extractedText, contains('executing instance of a program'));
      expect(result.extractedText, contains('Virtual memory isolates processes'));
    });

    test('rejects blank/image-only PDF honestly without claiming OCR', () async {
      final bytes = await _createSamplePdfBytes(pageTexts: ['']);

      await expectLater(
        () => extractor.extract(_pickedFromBytes(name: 'scan.pdf', bytes: bytes)),
        throwsA(
          isA<AppFailure>()
              .having((f) => f.code, 'code', 'PDF_NO_EXTRACTABLE_TEXT'),
        ),
      );
    });

    test('rejects password-protected PDF cleanly', () async {
      final bytes = await _createSamplePdfBytes(
        pageTexts: [
          'Protected study document text that should not be unlocked without password.',
        ],
        userPassword: 'secret-password',
      );

      await expectLater(
        () => extractor.extract(_pickedFromBytes(name: 'locked.pdf', bytes: bytes)),
        throwsA(
          isA<AppFailure>()
              .having((f) => f.code, 'code', 'PDF_PASSWORD_PROTECTED'),
        ),
      );
    });

    test('rejects corrupt PDF bytes cleanly', () async {
      final corruptBytes = Uint8List.fromList(
        utf8.encode('%PDF-1.7\nthis-is-corrupted-binary-content'),
      );

      await expectLater(
        () => extractor.extract(_pickedFromBytes(name: 'broken.pdf', bytes: corruptBytes)),
        throwsA(
          isA<AppFailure>().having((f) => f.code, 'code', 'PDF_CORRUPT'),
        ),
      );
    });
  });
}
