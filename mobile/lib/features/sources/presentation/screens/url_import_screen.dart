import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../../app/router/app_router.dart';
import '../../../../app/theme/app_spacing.dart';
import '../../../../core/widgets/app_text_field.dart';
import '../../../../core/widgets/error_view.dart';
import '../../../../core/widgets/primary_button.dart';
import '../../../../core/widgets/secondary_button.dart';
import '../../data/services/source_validation.dart';
import '../controllers/url_ingestion_state.dart';
import '../providers/source_providers.dart';

class UrlImportScreen extends ConsumerStatefulWidget {
  const UrlImportScreen({super.key});

  @override
  ConsumerState<UrlImportScreen> createState() => _UrlImportScreenState();
}

class _UrlImportScreenState extends ConsumerState<UrlImportScreen> {
  final _formKey = GlobalKey<FormState>();
  final _urlController = TextEditingController();

  @override
  void dispose() {
    _urlController.dispose();
    super.dispose();
  }

  Future<void> _pasteFromClipboard() async {
    final data = await Clipboard.getData(Clipboard.kTextPlain);
    final pasted = data?.text?.trim();
    if (pasted == null || pasted.isEmpty || !mounted) return;
    _urlController.text = pasted;
    _urlController.selection = TextSelection.collapsed(offset: pasted.length);
    ref.read(urlIngestionControllerProvider.notifier).clearError();
  }

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) {
      return;
    }
    await ref
        .read(urlIngestionControllerProvider.notifier)
        .submitUrl(_urlController.text);
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(urlIngestionControllerProvider);
    final controller = ref.read(urlIngestionControllerProvider.notifier);
    final theme = Theme.of(context);
    final colors = theme.colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Add Article Link'),
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
                if (state.step == UrlIngestionStep.failure &&
                    state.failure != null) ...[
                  InlineMessageBanner(
                    key: const Key('url_error_banner'),
                    message: state.failure!.message,
                    isError: true,
                    onDismiss: controller.clearError,
                  ),
                  const SizedBox(height: AppSpacing.md),
                ],
                if (state.step == UrlIngestionStep.success &&
                    state.createdSource != null)
                  _UrlSuccessCard(
                    title: state.createdSource!.title,
                    charCount: state.createdSource!.contentLength,
                    onViewSource: () {
                      final id = state.createdSource!.id;
                      context.pushReplacement(AppRoutes.sourceDetail(id));
                    },
                    onAddAnother: () {
                      _urlController.clear();
                      controller.reset();
                    },
                  )
                else
                  Card(
                    child: Padding(
                      padding: const EdgeInsets.all(AppSpacing.lg),
                      child: Form(
                        key: _formKey,
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            Text(
                              'Paste an article URL',
                              style: theme.textTheme.titleMedium,
                            ),
                            const SizedBox(height: AppSpacing.xxs),
                            Text(
                              'EduScroll securely fetches the web page on the server and extracts clean article text.',
                              style: theme.textTheme.bodySmall?.copyWith(
                                color: colors.onSurfaceVariant,
                              ),
                            ),
                            const SizedBox(height: AppSpacing.md),
                            AppTextField(
                              key: const Key('url_input_field'),
                              controller: _urlController,
                              label: 'Article URL',
                              hintText: 'https://example.org/articles/topic',
                              keyboardType: TextInputType.url,
                              textInputAction: TextInputAction.done,
                              enabled: !state.isBusy,
                              prefixIcon: Icons.link_rounded,
                              validator: SourceValidation.validateArticleUrl,
                              onChanged: (_) => controller.clearError(),
                              onFieldSubmitted: (_) => unawaited(_submit()),
                              suffixIcon: IconButton(
                                key: const Key('url_paste_button'),
                                tooltip: 'Paste link',
                                onPressed: state.isBusy
                                    ? null
                                    : () => unawaited(_pasteFromClipboard()),
                                icon: const Icon(Icons.content_paste_rounded),
                              ),
                            ),
                            const SizedBox(height: AppSpacing.md),
                            if (state.isBusy) ...[
                              Container(
                                key: const Key('url_processing_status'),
                                padding: const EdgeInsets.all(AppSpacing.md),
                                decoration: BoxDecoration(
                                  color: colors.surfaceContainerHighest,
                                  borderRadius: BorderRadius.circular(
                                    AppSpacing.radiusMd,
                                  ),
                                ),
                                child: Row(
                                  children: [
                                    const SizedBox(
                                      width: 20,
                                      height: 20,
                                      child: CircularProgressIndicator(
                                        strokeWidth: 2.4,
                                      ),
                                    ),
                                    const SizedBox(width: AppSpacing.sm + 4),
                                    Expanded(
                                      child: Text(
                                        state.step ==
                                                UrlIngestionStep.validating
                                            ? 'Validating link...'
                                            : 'Fetching & extracting article on EduScroll...',
                                        style: theme.textTheme.bodyMedium
                                            ?.copyWith(
                                          color: colors.onSurface,
                                          fontWeight: FontWeight.w500,
                                        ),
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                              const SizedBox(height: AppSpacing.sm),
                              TextButton(
                                key: const Key('url_cancel_button'),
                                onPressed: controller.cancelActiveSubmission,
                                child: const Text('Cancel'),
                              ),
                            ] else
                              PrimaryButton(
                                key: const Key('url_submit_button'),
                                label: 'Import article',
                                icon: Icons.download_done_rounded,
                                onPressed: () => unawaited(_submit()),
                              ),
                          ],
                        ),
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

class _UrlSuccessCard extends StatelessWidget {
  const _UrlSuccessCard({
    required this.title,
    required this.charCount,
    required this.onViewSource,
    required this.onAddAnother,
  });

  final String title;
  final int charCount;
  final VoidCallback onViewSource;
  final VoidCallback onAddAnother;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;

    return Card(
      key: const Key('url_success_card'),
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.lg),
        child: Column(
          children: [
            Container(
              width: 56,
              height: 56,
              decoration: BoxDecoration(
                color: colors.secondaryContainer,
                shape: BoxShape.circle,
              ),
              child: Icon(
                Icons.check_rounded,
                size: 32,
                color: colors.onSecondaryContainer,
              ),
            ),
            const SizedBox(height: AppSpacing.md),
            Text(
              'Article saved to your library',
              style: theme.textTheme.titleLarge,
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: AppSpacing.xs),
            Text(
              title,
              style: theme.textTheme.titleMedium?.copyWith(
                color: colors.primary,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: AppSpacing.xs),
            Text(
              '$charCount characters extracted',
              style: theme.textTheme.bodySmall?.copyWith(
                color: colors.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: AppSpacing.lg),
            PrimaryButton(
              key: const Key('url_open_source_button'),
              label: 'Open source',
              icon: Icons.article_outlined,
              onPressed: onViewSource,
            ),
            const SizedBox(height: AppSpacing.sm),
            SecondaryButton(
              key: const Key('url_import_another_button'),
              label: 'Add another link',
              onPressed: onAddAnother,
            ),
          ],
        ),
      ),
    );
  }
}
