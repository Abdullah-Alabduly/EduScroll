enum SourceStatus {
  completed('Completed', 'Ready'),
  failed('Failed', 'Failed');

  const SourceStatus(this.apiValue, this.displayLabel);

  final String apiValue;
  final String displayLabel;

  static SourceStatus fromApi(String raw) {
    switch (raw.trim().toLowerCase()) {
      case 'completed':
        return SourceStatus.completed;
      case 'failed':
        return SourceStatus.failed;
      default:
        throw FormatException('Unknown source status: "$raw".');
    }
  }
}
