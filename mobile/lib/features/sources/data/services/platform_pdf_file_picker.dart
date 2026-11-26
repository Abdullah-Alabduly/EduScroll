import 'dart:typed_data';

import 'package:file_picker/file_picker.dart';

import '../../../../core/error/app_failure.dart';
import '../../domain/entities/picked_pdf_file.dart';
import '../../domain/services/pdf_file_picker.dart';

class PlatformPdfFilePicker implements PdfFilePicker {
  const PlatformPdfFilePicker();

  @override
  Future<PickedPdfFile?> pickPdf() async {
    final picked = await FilePicker.pickFile(
      dialogTitle: 'Select a PDF document',
      type: FileType.custom,
      allowedExtensions: const ['pdf'],
    );

    if (picked == null) {
      return null;
    }

    final knownSize = picked.lengthSync() ?? await picked.length();

    return PickedPdfFile(
      name: picked.name,
      extension: picked.extension,
      sizeBytes: knownSize,
      readBytes: (maxBytes) => _readBoundedBytes(picked, maxBytes),
    );
  }

  Future<Uint8List> _readBoundedBytes(PlatformFile file, int maxBytes) async {
    final builder = BytesBuilder(copy: false);
    var total = 0;

    await for (final chunk in file.readAsByteStream()) {
      total += chunk.length;
      if (total > maxBytes) {
        throw AppFailure(
          type: AppFailureType.payloadTooLarge,
          message:
              'This PDF is larger than the ${maxBytes ~/ (1024 * 1024)} MB limit. Please choose a smaller document.',
          code: 'PDF_FILE_TOO_LARGE',
        );
      }
      builder.add(chunk);
    }

    return builder.takeBytes();
  }
}
