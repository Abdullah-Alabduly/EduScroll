import '../entities/picked_pdf_file.dart';

abstract interface class PdfFilePicker {
  /// Opens the platform picker restricted to PDF files.
  /// Returns `null` when the user cancels selection.
  Future<PickedPdfFile?> pickPdf();
}
