import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../../app/theme/app_spacing.dart';
import '../../../../core/error/error_mapper.dart';
import '../../../../core/widgets/error_view.dart';
import '../../../../core/widgets/loading_indicator.dart';
import '../../domain/entities/source_detail.dart';
import '../providers/source_providers.dart';
import '../widgets/source_type_badge.dart';

class SourceDetailScreen extends ConsumerStatefulWidget {
  const SourceDetailScreen({
    required this.sourceId,
    super.key,
  });

  final String sourceId;

  @override
  ConsumerState<SourceDetailScreen> createState() => _SourceDetailScreenState();
}

class _SourceDetailScreenState extends ConsumerState<SourceDetailScreen> {
  bool _isDeleting = false;

  Future<void> _confirmAndDelete(SourceDetail source) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Delete source'),
        content: Text(
          'Delete "${source.title}" permanently from your library?',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            key: const Key('detail_confirm_delete_button'),
            onPressed: () => Navigator.of(dialogContext).pop(true),
            child: const Text('Delete'),
          ),
        ],
      ),
    );

    if (confirmed != true || !mounted) return;

    setState(() => _isDeleting = true);
    try {
      await ref.read(sourceRepositoryProvider).deleteSource(source.id);
      ref
          .read(sourcesListControllerProvider.notifier)
          .removeFromList(source.id);

      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Source deleted.')),
      );
      context.pop();
    } catch (error) {
      if (!mounted) return;
      final failure = ErrorMapper.fromError(error);
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(failure.message)),
      );
    } finally {
      if (mounted) {
        setState(() => _isDeleting = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final detailAsync = ref.watch(sourceDetailProvider(widget.sourceId));

    return Scaffold(
      appBar: AppBar(
        title: const Text('Source Details'),
        actions: [
          if (detailAsync.valueOrNull != null)
            IconButton(
              key: const Key('detail_delete_button'),
              tooltip: 'Delete source',
              onPressed: _isDeleting
                  ? null
                  : () => unawaited(_confirmAndDelete(detailAsync.valueOrNull!)),
              icon: _isDeleting
                  ? const SizedBox(
                      width: 18,
                      height: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.delete_outline_rounded),
            ),
        ],
      ),
      body: SafeArea(
        child: detailAsync.when(
          loading: () =>
              const LoadingIndicator(message: 'Loading source content...'),
          error: (error, _) {
            final failure = ErrorMapper.fromError(error);
            return ErrorView(
              title: 'Could not open source',
              message: failure.message,
              onRetry: () => ref.invalidate(sourceDetailProvider(widget.sourceId)),
            );
          },
          data: (source) => _SourceDetailBody(source: source),
        ),
      ),
    );
  }
}

class _SourceDetailBody extends StatelessWidget {
  const _SourceDetailBody({required this.source});

  final SourceDetail source;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;

    return Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: AppSpacing.maxContentWidth),
        child: ListView(
          padding: AppSpacing.screenPadding,
          children: [
            Card(
              child: Padding(
                padding: const EdgeInsets.all(AppSpacing.lg),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Wrap(
                      spacing: AppSpacing.xs,
                      runSpacing: AppSpacing.xs,
                      crossAxisAlignment: WrapCrossAlignment.center,
                      children: [
                        SourceTypeBadge(type: source.sourceType),
                        SourceStatusChip(status: source.status),
                      ],
                    ),
                    const SizedBox(height: AppSpacing.sm + 4),
                    Text(
                      source.title,
                      key: const Key('detail_source_title'),
                      style: theme.textTheme.headlineSmall,
                    ),
                    const SizedBox(height: AppSpacing.sm),
                    Wrap(
                      spacing: AppSpacing.md,
                      runSpacing: AppSpacing.xs,
                      children: [
                        _MetaItem(
                          icon: Icons.calendar_today_outlined,
                          label: _formatDateTime(source.createdAtUtc),
                        ),
                        _MetaItem(
                          icon: Icons.notes_rounded,
                          label: '${source.contentLength} characters',
                        ),
                      ],
                    ),
                    if (source.originalUrl != null &&
                        source.originalUrl!.isNotEmpty) ...[
                      const SizedBox(height: AppSpacing.sm),
                      _MetaItem(
                        icon: Icons.link_rounded,
                        label: source.originalUrl!,
                      ),
                    ],
                    if (source.originalFileName != null &&
                        source.originalFileName!.isNotEmpty) ...[
                      const SizedBox(height: AppSpacing.sm),
                      _MetaItem(
                        icon: Icons.insert_drive_file_outlined,
                        label: source.originalFileName!,
                      ),
                    ],
                  ],
                ),
              ),
            ),
            const SizedBox(height: AppSpacing.md),
            Card(
              child: Padding(
                padding: const EdgeInsets.all(AppSpacing.lg),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'Extracted Text',
                      style: theme.textTheme.titleMedium?.copyWith(
                        color: colors.primary,
                      ),
                    ),
                    const SizedBox(height: AppSpacing.sm + 4),
                    // Rendered strictly as plain selectable text — never as HTML.
                    SelectableText(
                      source.extractedText,
                      key: const Key('detail_extracted_text'),
                      style: theme.textTheme.bodyLarge?.copyWith(
                        height: 1.65,
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  static String _formatDateTime(DateTime utc) {
    final local = utc.toLocal();
    final month = local.month.toString().padLeft(2, '0');
    final day = local.day.toString().padLeft(2, '0');
    final hour = local.hour.toString().padLeft(2, '0');
    final minute = local.minute.toString().padLeft(2, '0');
    return '${local.year}-$month-$day $hour:$minute';
  }
}

class _MetaItem extends StatelessWidget {
  const _MetaItem({
    required this.icon,
    required this.label,
  });

  final IconData icon;
  final String label;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;

    return Row(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.only(top: 2),
          child: Icon(icon, size: 15, color: colors.onSurfaceVariant),
        ),
        const SizedBox(width: AppSpacing.xxs + 2),
        Flexible(
          child: Text(
            label,
            style: theme.textTheme.bodySmall?.copyWith(
              color: colors.onSurfaceVariant,
            ),
          ),
        ),
      ],
    );
  }
}
