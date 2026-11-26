import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../../app/router/app_router.dart';
import '../../../../app/theme/app_spacing.dart';
import '../../../../core/widgets/app_text_field.dart';
import '../../../../core/widgets/error_view.dart';
import '../../../../core/widgets/primary_button.dart';
import '../../../../core/widgets/secondary_button.dart';
import '../controllers/pdf_ingestion_state.dart';
import '../providers/source_providers.dart';

class PdfImportScreen extends ConsumerStatefulWidget {
  const PdfImportScreen({super.key});

  @override
  ConsumerState<PdfImportScreen> createState() => _PdfImportScreenState();
}

class _PdfImportScreenState extends ConsumerState<PdfImportScreen> {
  final _titleController = TextEditingController();

  @override
  void dispose() {
    _titleController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(pdfIngestionControllerProvider);
    final controller = ref.read(pdfIngestionControllerProvider.notifier);
    final theme = Theme.of(context);
    final colors = theme.colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Import PDF'),
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
                if (state.step == PdfIngestionStep.failure &&
                    state.failure != null) ...[
                  InlineMessageBanner(
                    key: const Key('pdf_error_banner'),
                    message: state.failure!.message,
                    isError: true,
                  ),
                  const SizedBox(height: AppSpacing.md),
                ],
                if (state.step == PdfIngestionStep.success &&
                    state.createdSource != null)
                  _PdfSuccessCard(
                    title: state.createdSource!.title,
                    charCount: state.createdSource!.contentLength,
                    pageCount: state.extractedContent?.pageCount,
                    onViewSource: () {
                      final id = state.createdSource!.id;
                      context.pushReplacement(AppRoutes.sourceDetail(id));
                    },
                    onImportAnother: () {
                      _titleController.clear();
                      controller.reset();
                    },
                  )
                else ...[
                  Card(
                    child: Padding(
                      padding: const EdgeInsets.all(AppSpacing.lg),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Row(
                            children: [
                              Container(
                                width: 44,
                                height: 44,
                                decoration: BoxDecoration(
                                  color: colors.primaryContainer,
                                  borderRadius: BorderRadius.circular(
                                    AppSpacing.radiusMd,
                                  ),
                                ),
                                child: Icon(
                                  Icons.picture_as_pdf_rounded,
                                  color: colors.onPrimaryContainer,
                                ),
                              ),
                              const SizedBox(width: AppSpacing.md),
                              Expanded(
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text(
                                      state.selectedFile == null
                                          ? 'Choose a PDF document'
                                          : state.selectedFile!.name,
                                      key: const Key('pdf_selected_file_name'),
                                      maxLines: 2,
                                      overflow: TextOverflow.ellipsis,
                                      style: theme.textTheme.titleMedium,
                                    ),
                                    const SizedBox(height: 2),
                                    Text(
                                      state.selectedFile == null
                                          ? 'Up to 15 MB · Text extracted locally on device'
                                          : state.selectedFile!.formattedSize,
                                      key: const Key('pdf_selected_file_size'),
                                      style: theme.textTheme.bodySmall?.copyWith(
                                        color: colors.onSurfaceVariant,
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                            ],
                          ),
                          const SizedBox(height: AppSpacing.md),
                          if (state.selectedFile == null)
                            PrimaryButton(
                              key: const Key('pdf_choose_file_button'),
                              label: 'Choose PDF',
                              icon: Icons.upload_file_rounded,
                              isLoading:
                                  state.step == PdfIngestionStep.selectingFile,
                              onPressed: state.isBusy
                                  ? null
                                  : () => unawaited(controller.selectPdf()),
                            )
                          else
                            SecondaryButton(
                              key: const Key('pdf_change_file_button'),
                              label: 'Choose a different PDF',
                              icon: Icons.swap_horiz_rounded,
                              onPressed: state.isBusy
                                  ? null
                                  : () {
                                      _titleController.clear();
                                      unawaited(controller.selectPdf());
                                    },
                            ),
                        ],
                      ),
                    ),
                  ),
                  if (state.selectedFile != null) ...[
                    const SizedBox(height: AppSpacing.md),
                    Card(
                      child: Padding(
                        padding: const EdgeInsets.all(AppSpacing.lg),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            AppTextField(
                              key: const Key('pdf_title_field'),
                              controller: _titleController,
                              label: 'Title (optional)',
                              hintText:
                                  'Leave blank to use PDF title or file name',
                              enabled: !state.isBusy,
                              prefixIcon: Icons.title_rounded,
                            ),
                            const SizedBox(height: AppSpacing.md),
                            if (state.isBusy)
                              _PdfProcessingBanner(
                                step: state.step,
                                onCancel: controller.cancelActiveOperation,
                              )
                            else
                              PrimaryButton(
                                key: const Key('pdf_extract_submit_button'),
                                label: state.step == PdfIngestionStep.failure
                                    ? 'Retry extraction & save'
                                    : 'Extract text & save source',
                                icon: Icons.check_circle_outline_rounded,
                                onPressed: () => unawaited(
                                  controller.extractAndSubmit(
                                    customTitle: _titleController.text,
                                  ),
                                ),
                              ),
                          ],
                        ),
                      ),
                    ),
                  ],
                  const SizedBox(height: AppSpacing.md),
                  Text(
                    'Only extracted UTF-8 text and basic metadata are sent to EduScroll. Your original PDF stays on your device.',
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: colors.onSurfaceVariant,
                    ),
                    textAlign: TextAlign.center,
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _PdfProcessingBanner extends StatelessWidget {
  const _PdfProcessingBanner({
    required this.step,
    required this.onCancel,
  });

  final PdfIngestionStep step;
  final VoidCallback onCancel;

  String get _statusLabel {
    switch (step) {
      case PdfIngestionStep.selectingFile:
        return 'Opening file picker...';
      case PdfIngestionStep.extracting:
        return 'Extracting text locally on device...';
      case PdfIngestionStep.preparing:
        return 'Preparing source text...';
      case PdfIngestionStep.submitting:
        return 'Saving source to EduScroll...';
      default:
        return 'Processing...';
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Container(
          key: const Key('pdf_processing_status'),
          padding: const EdgeInsets.all(AppSpacing.md),
          decoration: BoxDecoration(
            color: colors.surfaceContainerHighest,
            borderRadius: BorderRadius.circular(AppSpacing.radiusMd),
          ),
          child: Row(
            children: [
              const SizedBox(
                width: 20,
                height: 20,
                child: CircularProgressIndicator(strokeWidth: 2.4),
              ),
              const SizedBox(width: AppSpacing.sm + 4),
              Expanded(
                child: Text(
                  _statusLabel,
                  style: theme.textTheme.bodyMedium?.copyWith(
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
          key: const Key('pdf_cancel_button'),
          onPressed: onCancel,
          child: const Text('Cancel'),
        ),
      ],
    );
  }
}

class _PdfSuccessCard extends StatelessWidget {
  const _PdfSuccessCard({
    required this.title,
    required this.charCount,
    required this.onViewSource,
    required this.onImportAnother,
    this.pageCount,
  });

  final String title;
  final int charCount;
  final int? pageCount;
  final VoidCallback onViewSource;
  final VoidCallback onImportAnother;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;

    return Card(
      key: const Key('pdf_success_card'),
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
              'PDF saved to your library',
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
              pageCount != null && pageCount! > 0
                  ? '$pageCount page${pageCount == 1 ? '' : 's'} · $charCount characters extracted'
                  : '$charCount characters extracted',
              style: theme.textTheme.bodySmall?.copyWith(
                color: colors.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: AppSpacing.lg),
            PrimaryButton(
              key: const Key('pdf_open_source_button'),
              label: 'Open source',
              icon: Icons.article_outlined,
              onPressed: onViewSource,
            ),
            const SizedBox(height: AppSpacing.sm),
            SecondaryButton(
              key: const Key('pdf_import_another_button'),
              label: 'Import another PDF',
              onPressed: onImportAnother,
            ),
          ],
        ),
      ),
    );
  }
}
