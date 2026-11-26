import '../../domain/entities/source_status.dart';
import '../../domain/entities/source_summary.dart';
import '../../domain/entities/source_type.dart';

abstract final class SourceSummaryModel {
  static SourceSummary fromJson(Map<String, dynamic> json) {
    final id = json['id']?.toString().trim() ?? '';
    final title = json['title']?.toString().trim() ?? '';
    final sourceTypeRaw = json['sourceType']?.toString() ?? '';
    final statusRaw = json['status']?.toString() ?? '';
    final createdRaw = json['createdAtUtc']?.toString() ?? '';
    final updatedRaw = json['updatedAtUtc']?.toString() ?? '';
    final contentLengthRaw = json['contentLength'];

    if (id.isEmpty || title.isEmpty || contentLengthRaw is! num) {
      throw const FormatException('Invalid SourceSummaryDto payload.');
    }

    return SourceSummary(
      id: id,
      title: title,
      sourceType: SourceType.fromApi(sourceTypeRaw),
      originalUrl: json['originalUrl']?.toString(),
      originalFileName: json['originalFileName']?.toString(),
      contentLength: contentLengthRaw.toInt(),
      textPreview: json['textPreview']?.toString() ?? '',
      status: SourceStatus.fromApi(statusRaw),
      createdAtUtc: DateTime.parse(createdRaw).toUtc(),
      updatedAtUtc: DateTime.parse(updatedRaw).toUtc(),
    );
  }
}
