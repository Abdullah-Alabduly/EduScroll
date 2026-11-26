import '../../domain/entities/paged_sources.dart';
import '../../domain/entities/source_summary.dart';
import 'source_summary_model.dart';

abstract final class PagedSourcesModel {
  static PagedSources fromJson(Map<String, dynamic> json) {
    final rawItems = json['items'];
    final page = json['page'];
    final pageSize = json['pageSize'];
    final totalCount = json['totalCount'];
    final hasNextPage = json['hasNextPage'];

    if (rawItems is! List ||
        page is! num ||
        pageSize is! num ||
        totalCount is! num ||
        hasNextPage is! bool) {
      throw const FormatException('Invalid PagedSourcesResponse payload.');
    }

    final items = <SourceSummary>[];
    for (final item in rawItems) {
      if (item is Map<String, dynamic>) {
        items.add(SourceSummaryModel.fromJson(item));
      } else if (item is Map) {
        items.add(
          SourceSummaryModel.fromJson(
            item.map((k, v) => MapEntry(k.toString(), v)),
          ),
        );
      } else {
        throw const FormatException('Invalid source item in PagedSourcesResponse.');
      }
    }

    return PagedSources(
      items: List<SourceSummary>.unmodifiable(items),
      page: page.toInt(),
      pageSize: pageSize.toInt(),
      totalCount: totalCount.toInt(),
      hasNextPage: hasNextPage,
    );
  }
}
