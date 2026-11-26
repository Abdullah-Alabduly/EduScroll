import 'package:flutter/foundation.dart';

import '../../../../core/error/app_failure.dart';
import '../../domain/entities/source_summary.dart';

enum SourcesListStatus {
  initial,
  loading,
  loaded,
  error,
}

@immutable
class SourcesListState {
  const SourcesListState({
    required this.status,
    this.items = const [],
    this.page = 1,
    this.pageSize = 20,
    this.totalCount = 0,
    this.hasNextPage = false,
    this.isRefreshing = false,
    this.isLoadingMore = false,
    this.deletingIds = const {},
    this.failure,
  });

  const SourcesListState.initial() : this(status: SourcesListStatus.initial);

  final SourcesListStatus status;
  final List<SourceSummary> items;
  final int page;
  final int pageSize;
  final int totalCount;
  final bool hasNextPage;
  final bool isRefreshing;
  final bool isLoadingMore;
  final Set<String> deletingIds;
  final AppFailure? failure;

  bool get hasLoadedOnce => status == SourcesListStatus.loaded;

  SourcesListState copyWith({
    SourcesListStatus? status,
    List<SourceSummary>? items,
    int? page,
    int? pageSize,
    int? totalCount,
    bool? hasNextPage,
    bool? isRefreshing,
    bool? isLoadingMore,
    Set<String>? deletingIds,
    AppFailure? failure,
    bool clearFailure = false,
  }) {
    return SourcesListState(
      status: status ?? this.status,
      items: items ?? this.items,
      page: page ?? this.page,
      pageSize: pageSize ?? this.pageSize,
      totalCount: totalCount ?? this.totalCount,
      hasNextPage: hasNextPage ?? this.hasNextPage,
      isRefreshing: isRefreshing ?? this.isRefreshing,
      isLoadingMore: isLoadingMore ?? this.isLoadingMore,
      deletingIds: deletingIds ?? this.deletingIds,
      failure: clearFailure ? null : (failure ?? this.failure),
    );
  }
}
