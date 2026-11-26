import 'package:flutter/foundation.dart';

import 'source_status.dart';
import 'source_type.dart';

@immutable
class SourceSummary {
  const SourceSummary({
    required this.id,
    required this.title,
    required this.sourceType,
    required this.contentLength,
    required this.textPreview,
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
  final int contentLength;
  final String textPreview;
  final SourceStatus status;
  final DateTime createdAtUtc;
  final DateTime updatedAtUtc;

  @override
  bool operator ==(Object other) {
    if (identical(this, other)) return true;
    return other is SourceSummary &&
        other.id == id &&
        other.title == title &&
        other.sourceType == sourceType &&
        other.originalUrl == originalUrl &&
        other.originalFileName == originalFileName &&
        other.contentLength == contentLength &&
        other.textPreview == textPreview &&
        other.status == status &&
        other.createdAtUtc == createdAtUtc &&
        other.updatedAtUtc == updatedAtUtc;
  }

  @override
  int get hashCode => Object.hash(
        id,
        title,
        sourceType,
        originalUrl,
        originalFileName,
        contentLength,
        textPreview,
        status,
        createdAtUtc,
        updatedAtUtc,
      );
}
