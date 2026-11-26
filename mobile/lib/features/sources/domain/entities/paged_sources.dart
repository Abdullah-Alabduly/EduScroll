import 'package:flutter/foundation.dart';

import 'source_summary.dart';

@immutable
class PagedSources {
  const PagedSources({
    required this.items,
    required this.page,
    required this.pageSize,
    required this.totalCount,
    required this.hasNextPage,
  });

  final List<SourceSummary> items;
  final int page;
  final int pageSize;
  final int totalCount;
  final bool hasNextPage;
}
