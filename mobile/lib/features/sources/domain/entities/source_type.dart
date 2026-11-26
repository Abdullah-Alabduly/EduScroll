enum SourceType {
  pdf('Pdf', 'PDF'),
  webArticle('WebArticle', 'Article');

  const SourceType(this.apiValue, this.displayLabel);

  final String apiValue;
  final String displayLabel;

  static SourceType fromApi(String raw) {
    switch (raw.trim().toLowerCase()) {
      case 'pdf':
        return SourceType.pdf;
      case 'webarticle':
      case 'web_article':
      case 'article':
        return SourceType.webArticle;
      default:
        throw FormatException('Unknown source type: "$raw".');
    }
  }
}
