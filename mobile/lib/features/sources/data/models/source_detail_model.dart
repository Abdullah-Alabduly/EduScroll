import '../../domain/entities/source_detail.dart';
import '../../domain/entities/source_status.dart';
import '../../domain/entities/source_type.dart';

abstract final class SourceDetailModel {
  static SourceDetail fromJson(Map<String, dynamic> json) {
    final id = json['id']?.toString().trim() ?? '';
    final title = json['title']?.toString().trim() ?? '';
    final sourceTypeRaw = json['sourceType']?.toString() ?? '';
    final extractedText = json['extractedText']?.toString();
    final statusRaw = json['status']?.toString() ?? '';
    final createdRaw = json['createdAtUtc']?.toString() ?? '';
    final updatedRaw = json['updatedAtUtc']?.toString() ?? '';
    final contentLengthRaw = json['contentLength'];

    if (id.isEmpty ||
        title.isEmpty ||
        extractedText == null ||
        contentLengthRaw is! num) {
      throw const FormatException('Invalid SourceDetailDto payload.');
    }

    return SourceDetail(
      id: id,
      title: title,
      sourceType: SourceType.fromApi(sourceTypeRaw),
      originalUrl: json['originalUrl']?.toString(),
      originalFileName: json['originalFileName']?.toString(),
      extractedText: extractedText,
      contentLength: contentLengthRaw.toInt(),
      status: SourceStatus.fromApi(statusRaw),
      createdAtUtc: DateTime.parse(createdRaw).toUtc(),
      updatedAtUtc: DateTime.parse(updatedRaw).toUtc(),
    );
  }
}
