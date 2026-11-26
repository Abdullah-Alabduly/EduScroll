import 'package:flutter/foundation.dart';

@immutable
class ExtractedPdfContent {
  const ExtractedPdfContent({
    required this.title,
    required this.extractedText,
    required this.pageCount,
    required this.originalFileName,
  });

  final String title;
  final String extractedText;
  final int pageCount;
  final String originalFileName;

  int get characterCount => extractedText.length;
}
