import '../entities/extracted_pdf_content.dart';
import '../entities/picked_pdf_file.dart';

abstract interface class PdfTextExtractorService {
  /// Validates the picked PDF file and extracts normalized UTF-8 text off the main UI thread.
  /// Throws [AppFailure] with user-friendly messages on size/signature/corrupt/encrypted/empty errors.
  Future<ExtractedPdfContent> extract(PickedPdfFile file);
}
