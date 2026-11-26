import 'package:flutter/foundation.dart';

import 'source_status.dart';
import 'source_summary.dart';
import 'source_type.dart';

@immutable
class SourceDetail {
  const SourceDetail({
    required this.id,
    required this.title,
    required this.sourceType,
    required this.extractedText,
    required this.contentLength,
    required this.status,
    required this.createdAtUtc,
    required this.updatedAtUtc,
    this.originalUrl,
    this.originalFileName,
  });

  final String id;
  final String title;
  final SourceType sourceType;
  final String? originalUrl;
  final String? originalFileName;
  final String extractedText;
  final int contentLength;
  final SourceStatus status;
  final DateTime createdAtUtc;
  final DateTime updatedAtUtc;

  SourceSummary toSummary({int previewLength = 240}) {
    final flat = extractedText.replaceAll(RegExp(r'\s+'), ' ').trim();
    final preview = flat.length <= previewLength
        ? flat
        : '${flat.substring(0, previewLength - 1).trimRight()}…';

    return SourceSummary(
      id: id,
      title: title,
      sourceType: sourceType,
      originalUrl: originalUrl,
      originalFileName: originalFileName,
      contentLength: contentLength,
      textPreview: preview,
      status: status,
      createdAtUtc: createdAtUtc,
      updatedAtUtc: updatedAtUtc,
    );
  }
}
