import 'package:flutter/material.dart';

import '../../../../app/theme/app_spacing.dart';
import '../../domain/entities/source_status.dart';
import '../../domain/entities/source_type.dart';

class SourceTypeBadge extends StatelessWidget {
  const SourceTypeBadge({
    required this.type,
    super.key,
  });

  final SourceType type;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;
    final isPdf = type == SourceType.pdf;

    final bgColor = isPdf ? colors.primaryContainer : colors.secondaryContainer;
    final fgColor =
        isPdf ? colors.onPrimaryContainer : colors.onSecondaryContainer;
    final icon =
        isPdf ? Icons.picture_as_pdf_rounded : Icons.language_rounded;

    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.xs + 2,
        vertical: AppSpacing.xxs,
      ),
      decoration: BoxDecoration(
        color: bgColor,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 14, color: fgColor),
          const SizedBox(width: AppSpacing.xxs),
          Text(
            type.displayLabel,
            style: theme.textTheme.labelSmall?.copyWith(
              color: fgColor,
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
      ),
    );
  }
}

class SourceStatusChip extends StatelessWidget {
  const SourceStatusChip({
    required this.status,
    super.key,
  });

  final SourceStatus status;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;
    final isFailed = status == SourceStatus.failed;

    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.xs + 2,
        vertical: AppSpacing.xxs,
      ),
      decoration: BoxDecoration(
        color: isFailed
            ? colors.errorContainer
            : colors.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        status.displayLabel,
        style: theme.textTheme.labelSmall?.copyWith(
          color: isFailed ? colors.onErrorContainer : colors.onSurfaceVariant,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}
