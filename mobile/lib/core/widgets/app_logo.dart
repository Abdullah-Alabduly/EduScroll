import 'package:flutter/material.dart';

import '../../app/theme/app_spacing.dart';

class AppLogo extends StatelessWidget {
  const AppLogo({
    super.key,
    this.compact = false,
    this.subtitle,
  });

  final bool compact;
  final String? subtitle;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;

    if (compact) {
      return Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          _BrandMark(size: 32, colors: colors),
          const SizedBox(width: AppSpacing.xs),
          Text(
            'EduScroll',
            style: theme.textTheme.titleLarge?.copyWith(
              fontWeight: FontWeight.w800,
              letterSpacing: -0.4,
            ),
          ),
        ],
      );
    }

    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        _BrandMark(size: 56, colors: colors),
        const SizedBox(height: AppSpacing.sm),
        Text(
          'EduScroll',
          style: theme.textTheme.headlineMedium?.copyWith(
            fontWeight: FontWeight.w800,
            letterSpacing: -0.6,
          ),
        ),
        if (subtitle != null) ...[
          const SizedBox(height: AppSpacing.xxs),
          Text(
            subtitle!,
            textAlign: TextAlign.center,
            style: theme.textTheme.bodyMedium,
          ),
        ],
      ],
    );
  }
}

class _BrandMark extends StatelessWidget {
  const _BrandMark({
    required this.size,
    required this.colors,
  });

  final double size;
  final ColorScheme colors;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      label: 'EduScroll logo',
      child: Container(
        width: size,
        height: size,
        decoration: BoxDecoration(
          color: colors.primaryContainer,
          borderRadius: BorderRadius.circular(size * 0.28),
        ),
        child: Icon(
          Icons.play_lesson_rounded,
          size: size * 0.56,
          color: colors.primary,
        ),
      ),
    );
  }
}
