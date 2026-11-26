import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../../app/router/app_router.dart';
import '../../../../app/theme/app_spacing.dart';

class AddSourceScreen extends StatelessWidget {
  const AddSourceScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Add Source'),
      ),
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(
              maxWidth: AppSpacing.maxContentWidth,
            ),
            child: ListView(
              padding: AppSpacing.screenPadding,
              children: [
                Text(
                  'Bring your study material',
                  style: theme.textTheme.headlineSmall,
                ),
                const SizedBox(height: AppSpacing.xs),
                Text(
                  'Import a PDF from your device or paste a public article link to save clean study text to your EduScroll library.',
                  style: theme.textTheme.bodyMedium,
                ),
                const SizedBox(height: AppSpacing.lg),
                _IngestionOptionCard(
                  cardKey: const Key('add_source_upload_pdf_card'),
                  icon: Icons.picture_as_pdf_rounded,
                  badge: 'On-device extraction',
                  title: 'Upload PDF',
                  subtitle:
                      'Select a text-based PDF (up to 15 MB). Text is extracted privately on your device — the PDF file itself is never uploaded.',
                  accentColor: colors.primary,
                  containerColor: colors.primaryContainer,
                  onContainerColor: colors.onPrimaryContainer,
                  onTap: () => context.push(AppRoutes.addSourcePdf),
                ),
                const SizedBox(height: AppSpacing.md),
                _IngestionOptionCard(
                  cardKey: const Key('add_source_add_url_card'),
                  icon: Icons.link_rounded,
                  badge: 'Secure article fetch',
                  title: 'Add Article Link',
                  subtitle:
                      'Paste any public HTTP or HTTPS article URL. EduScroll fetches and extracts the main readable text for your library.',
                  accentColor: colors.secondary,
                  containerColor: colors.secondaryContainer,
                  onContainerColor: colors.onSecondaryContainer,
                  onTap: () => context.push(AppRoutes.addSourceUrl),
                ),
                const SizedBox(height: AppSpacing.lg),
                Card(
                  color: colors.surfaceContainerHighest.withValues(alpha: 0.45),
                  child: Padding(
                    padding: const EdgeInsets.all(AppSpacing.md),
                    child: Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Icon(
                          Icons.shield_outlined,
                          size: 20,
                          color: colors.primary,
                        ),
                        const SizedBox(width: AppSpacing.sm),
                        Expanded(
                          child: Text(
                            'Your sources stay private to your account. Scanned image-only PDFs and login-walled web pages are not supported yet.',
                            style: theme.textTheme.bodySmall?.copyWith(
                              color: colors.onSurfaceVariant,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _IngestionOptionCard extends StatelessWidget {
  const _IngestionOptionCard({
    required this.cardKey,
    required this.icon,
    required this.badge,
    required this.title,
    required this.subtitle,
    required this.accentColor,
    required this.containerColor,
    required this.onContainerColor,
    required this.onTap,
  });

  final Key cardKey;
  final IconData icon;
  final String badge;
  final String title;
  final String subtitle;
  final Color accentColor;
  final Color containerColor;
  final Color onContainerColor;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;

    return Card(
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        key: cardKey,
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.lg),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Container(
                    width: 48,
                    height: 48,
                    decoration: BoxDecoration(
                      color: containerColor,
                      borderRadius: BorderRadius.circular(AppSpacing.radiusMd),
                    ),
                    child: Icon(icon, size: 26, color: onContainerColor),
                  ),
                  const SizedBox(width: AppSpacing.md),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Container(
                          padding: const EdgeInsets.symmetric(
                            horizontal: AppSpacing.xs + 2,
                            vertical: 2,
                          ),
                          decoration: BoxDecoration(
                            color: containerColor,
                            borderRadius: BorderRadius.circular(999),
                          ),
                          child: Text(
                            badge,
                            style: theme.textTheme.labelSmall?.copyWith(
                              color: onContainerColor,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                        ),
                        const SizedBox(height: AppSpacing.xxs),
                        Text(title, style: theme.textTheme.titleLarge),
                      ],
                    ),
                  ),
                  Icon(
                    Icons.arrow_forward_rounded,
                    color: accentColor,
                  ),
                ],
              ),
              const SizedBox(height: AppSpacing.sm + 4),
              Text(
                subtitle,
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: colors.onSurfaceVariant,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
