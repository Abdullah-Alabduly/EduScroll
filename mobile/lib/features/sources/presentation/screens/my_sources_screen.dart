import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../../app/router/app_router.dart';
import '../../../../app/theme/app_spacing.dart';
import '../../../../core/widgets/empty_state.dart';
import '../../../../core/widgets/error_view.dart';
import '../../../../core/widgets/loading_indicator.dart';
import '../../../../core/widgets/secondary_button.dart';
import '../../domain/entities/source_summary.dart';
import '../controllers/sources_list_state.dart';
import '../providers/source_providers.dart';
import '../widgets/source_card.dart';

class MySourcesScreen extends ConsumerStatefulWidget {
  const MySourcesScreen({super.key});

  @override
  ConsumerState<MySourcesScreen> createState() => _MySourcesScreenState();
}

class _MySourcesScreenState extends ConsumerState<MySourcesScreen> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        unawaited(
          ref.read(sourcesListControllerProvider.notifier).ensureLoaded(),
        );
      }
    });
  }

  Future<void> _confirmAndDelete(SourceSummary source) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Delete source'),
        content: Text(
          'Delete "${source.title}" from your library? This cannot be undone.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            key: const Key('confirm_delete_source_button'),
            onPressed: () => Navigator.of(dialogContext).pop(true),
            child: const Text('Delete'),
          ),
        ],
      ),
    );

    if (confirmed != true || !mounted) return;

    final failure = await ref
        .read(sourcesListControllerProvider.notifier)
        .deleteSource(source.id);

    if (!mounted) return;

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          failure == null ? 'Source deleted.' : failure.message,
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(sourcesListControllerProvider);
    final controller = ref.read(sourcesListControllerProvider.notifier);

    return Scaffold(
      appBar: AppBar(
        title: const Text('My Sources'),
        actions: [
          IconButton(
            key: const Key('sources_refresh_button'),
            tooltip: 'Refresh sources',
            onPressed: state.isRefreshing ||
                    state.status == SourcesListStatus.loading
                ? null
                : () => unawaited(controller.loadFirstPage(isRefresh: true)),
            icon: const Icon(Icons.refresh_rounded),
          ),
        ],
      ),
      body: SafeArea(
        child: _buildBody(context, state),
      ),
    );
  }

  Widget _buildBody(BuildContext context, SourcesListState state) {
    final controller = ref.read(sourcesListControllerProvider.notifier);

    if (state.status == SourcesListStatus.initial ||
        (state.status == SourcesListStatus.loading && state.items.isEmpty)) {
      return const LoadingIndicator(message: 'Loading your sources...');
    }

    if (state.status == SourcesListStatus.error && state.items.isEmpty) {
      return ErrorView(
        title: 'Could not load sources',
        message: state.failure?.message ?? 'Unable to load your library.',
        onRetry: () => unawaited(controller.loadFirstPage()),
      );
    }

    if (state.items.isEmpty) {
      return Center(
        child: SingleChildScrollView(
          padding: AppSpacing.screenPadding,
          child: ConstrainedBox(
            constraints: const BoxConstraints(
              maxWidth: AppSpacing.maxContentWidth,
            ),
            child: EmptyState(
              icon: Icons.library_books_outlined,
              badgeText: 'Your Library',
              title: 'No sources saved yet',
              description:
                  'Import a PDF or paste an article link to build your personal EduScroll study library.',
              actionLabel: 'Add your first source',
              onAction: () => context.go(AppRoutes.addSource),
            ),
          ),
        ),
      );
    }

    return Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: AppSpacing.maxContentWidth),
        child: RefreshIndicator(
          onRefresh: () => controller.loadFirstPage(isRefresh: true),
          child: ListView.separated(
            padding: AppSpacing.screenPadding,
            itemCount: state.items.length +
                (state.failure != null ? 1 : 0) +
                (state.hasNextPage ? 1 : 0),
            separatorBuilder: (_, _) => const SizedBox(height: AppSpacing.sm),
            itemBuilder: (context, index) {
              var offset = index;
              if (state.failure != null) {
                if (offset == 0) {
                  return InlineMessageBanner(
                    message: state.failure!.message,
                    isError: true,
                    onDismiss: controller.clearError,
                  );
                }
                offset -= 1;
              }

              if (offset < state.items.length) {
                final source = state.items[offset];
                return SourceCard(
                  source: source,
                  isDeleting: state.deletingIds.contains(source.id),
                  onTap: () =>
                      context.push(AppRoutes.sourceDetail(source.id)),
                  onDelete: () => unawaited(_confirmAndDelete(source)),
                );
              }

              return Padding(
                padding: const EdgeInsets.symmetric(vertical: AppSpacing.sm),
                child: SecondaryButton(
                  key: const Key('sources_load_more_button'),
                  label: state.isLoadingMore
                      ? 'Loading more...'
                      : 'Load more sources',
                  isLoading: state.isLoadingMore,
                  onPressed: state.isLoadingMore
                      ? null
                      : () => unawaited(controller.loadNextPage()),
                ),
              );
            },
          ),
        ),
      ),
    );
  }
}
