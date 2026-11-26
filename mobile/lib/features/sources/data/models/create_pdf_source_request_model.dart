import 'package:flutter/foundation.dart';

import '../../domain/entities/source_type.dart';

@immutable
class CreatePdfSourceRequestModel {
  const CreatePdfSourceRequestModel({
    required this.title,
    required this.extractedText,
    this.fileName,
  });

  final String title;
  final String extractedText;
  final String? fileName;

  Map<String, dynamic> toJson() => <String, dynamic>{
        'title': title.trim(),
        'sourceType': SourceType.pdf.apiValue,
        'extractedText': extractedText,
        if (fileName != null && fileName!.trim().isNotEmpty)
          'fileName': fileName!.trim(),
      };

  @override
  String toString() =>
      'CreatePdfSourceRequestModel(title: $title, textLength: ${extractedText.length})';
}
