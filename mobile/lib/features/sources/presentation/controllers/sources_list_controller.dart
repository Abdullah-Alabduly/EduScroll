import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/error/app_failure.dart';
import '../../../../core/error/error_mapper.dart';
import '../../../authentication/presentation/controllers/auth_state.dart';
import '../../../authentication/presentation/providers/auth_providers.dart';
import '../../domain/entities/source_detail.dart';
import '../../domain/entities/source_summary.dart';
import '../providers/source_providers.dart';
import 'sources_list_state.dart';

class SourcesListController extends Notifier<SourcesListState> {
  static const int defaultPageSize = 20;

  @override
  SourcesListState build() {
    // Isolate source state across sessions so logging out clears cached items immediately.
    ref.listen<AuthState>(authControllerProvider, (previous, next) {
      if (previous?.user?.id != next.user?.id || !next.isAuthenticated) {
        state = const SourcesListState.initial();
      }
    });
    return const SourcesListState.initial();
  }

  Future<void> ensureLoaded() async {
    if (state.status == SourcesListStatus.loading ||
        state.status == SourcesListStatus.loaded) {
      return;
    }
    await loadFirstPage();
  }

  Future<void> loadFirstPage({bool isRefresh = false}) async {
    if (isRefresh && state.isRefreshing) return;
    if (!isRefresh && state.status == SourcesListStatus.loading) return;

    if (isRefresh) {
      state = state.copyWith(isRefreshing: true, clearFailure: true);
    } else {
      state = state.copyWith(
        status: SourcesListStatus.loading,
        clearFailure: true,
      );
    }

    try {
      final paged = await ref.read(sourceRepositoryProvider).getSources(
            page: 1,
            pageSize: defaultPageSize,
          );

      state = SourcesListState(
        status: SourcesListStatus.loaded,
        items: paged.items,
        page: paged.page,
        pageSize: paged.pageSize,
        totalCount: paged.totalCount,
        hasNextPage: paged.hasNextPage,
      );
    } catch (error) {
      final failure = ErrorMapper.fromError(error);
      if (state.items.isNotEmpty) {
        state = state.copyWith(
          status: SourcesListStatus.loaded,
          isRefreshing: false,
          failure: failure,
        );
      } else {
        state = state.copyWith(
          status: SourcesListStatus.error,
          isRefreshing: false,
          failure: failure,
        );
      }
    }
  }

  Future<void> loadNextPage() async {
    if (!state.hasNextPage ||
        state.isLoadingMore ||
        state.isRefreshing ||
        state.status == SourcesListStatus.loading) {
      return;
    }

    state = state.copyWith(isLoadingMore: true, clearFailure: true);

    try {
      final nextPage = state.page + 1;
      final paged = await ref.read(sourceRepositoryProvider).getSources(
            page: nextPage,
            pageSize: state.pageSize,
          );

      final existingIds = state.items.map((item) => item.id).toSet();
      final merged = <SourceSummary>[
        ...state.items,
        ...paged.items.where((item) => !existingIds.contains(item.id)),
      ];

      state = state.copyWith(
        status: SourcesListStatus.loaded,
        items: List<SourceSummary>.unmodifiable(merged),
        page: paged.page,
        totalCount: paged.totalCount,
        hasNextPage: paged.hasNextPage,
        isLoadingMore: false,
      );
    } catch (error) {
      state = state.copyWith(
        isLoadingMore: false,
        failure: ErrorMapper.fromError(error),
      );
    }
  }

  void prependCreated(SourceDetail detail) {
    final summary = detail.toSummary();
    final filtered = state.items.where((item) => item.id != detail.id).toList();
    final updated = List<SourceSummary>.unmodifiable([summary, ...filtered]);
    final addedNew = filtered.length == state.items.length;

    state = state.copyWith(
      status: SourcesListStatus.loaded,
      items: updated,
      totalCount: addedNew ? state.totalCount + 1 : state.totalCount,
      clearFailure: true,
    );
  }

  Future<AppFailure?> deleteSource(String id) async {
    if (state.deletingIds.contains(id)) {
      return null;
    }

    state = state.copyWith(
      deletingIds: {...state.deletingIds, id},
      clearFailure: true,
    );

    try {
      await ref.read(sourceRepositoryProvider).deleteSource(id);
      removeFromList(id);
      return null;
    } catch (error) {
      final failure = ErrorMapper.fromError(error);
      final updatedDeleting = {...state.deletingIds}..remove(id);
      state = state.copyWith(
        deletingIds: updatedDeleting,
        failure: failure,
      );
      return failure;
    }
  }

  void removeFromList(String id) {
    final existed = state.items.any((item) => item.id == id);
    final updatedItems =
        state.items.where((item) => item.id != id).toList(growable: false);
    final updatedDeleting = {...state.deletingIds}..remove(id);

    state = state.copyWith(
      items: updatedItems,
      totalCount: existed && state.totalCount > 0
          ? state.totalCount - 1
          : state.totalCount,
      deletingIds: updatedDeleting,
    );
  }

  void clearError() {
    if (state.failure == null) return;
    state = state.copyWith(clearFailure: true);
  }
}
