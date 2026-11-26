import 'package:flutter/foundation.dart';

/// Represents a user-selected PDF file ready for local validation and extraction.
/// The raw bytes are kept strictly on-device and are never uploaded to the backend.
@immutable
class PickedPdfFile {
  const PickedPdfFile({
    required this.name,
    required this.sizeBytes,
    required this.readBytes,
    this.extension,
  });

  final String name;
  final int? sizeBytes;
  final String? extension;

  /// Lazily reads up to [maxBytes] bytes so oversized files are rejected before loading into RAM.
  final Future<Uint8List> Function(int maxBytes) readBytes;

  String get formattedSize {
    final size = sizeBytes;
    if (size == null) return 'Unknown size';
    if (size < 1024) return '$size B';
    if (size < 1024 * 1024) {
      return '${(size / 1024).toStringAsFixed(1)} KB';
    }
    return '${(size / (1024 * 1024)).toStringAsFixed(2)} MB';
  }
}
