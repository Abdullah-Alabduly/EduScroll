import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/error/app_failure.dart';
import '../../../../core/error/error_mapper.dart';
import '../../data/services/source_validation.dart';
import '../providers/source_providers.dart';
import 'url_ingestion_state.dart';

class UrlIngestionController extends AutoDisposeNotifier<UrlIngestionState> {
  int _operationId = 0;
  CancelToken? _activeCancelToken;

  @override
  UrlIngestionState build() {
    ref.onDispose(() {
      _operationId++;
      _activeCancelToken?.cancel('Screen disposed');
    });
    return const UrlIngestionState.initial();
  }

  Future<void> submitUrl(String rawUrl) async {
    if (state.isBusy) return;

    final trimmed = rawUrl.trim();
    final opId = ++_operationId;

    state = UrlIngestionState(
      step: UrlIngestionStep.validating,
      urlInput: trimmed,
    );

    final validationError = SourceValidation.validateArticleUrl(trimmed);
    if (validationError != null) {
      state = UrlIngestionState(
        step: UrlIngestionStep.failure,
        urlInput: trimmed,
        failure: AppFailure(
          type: AppFailureType.validation,
          message: validationError,
          code: 'URL_INVALID',
        ),
      );
      return;
    }

    final cancelToken = CancelToken();
    _activeCancelToken = cancelToken;

    state = UrlIngestionState(
      step: UrlIngestionStep.submitting,
      urlInput: trimmed,
    );

    try {
      final created = await ref.read(sourceRepositoryProvider).createUrlSource(
            url: trimmed,
            cancelToken: cancelToken,
          );

      if (_operationId != opId || cancelToken.isCancelled) return;

      ref.read(sourcesListControllerProvider.notifier).prependCreated(created);

      state = UrlIngestionState(
        step: UrlIngestionStep.success,
        urlInput: trimmed,
        createdSource: created,
      );
    } catch (error) {
      if (_operationId != opId) return;
      final failure = ErrorMapper.fromError(error);
      if (failure.type == AppFailureType.cancelled) {
        state = UrlIngestionState(
          step: UrlIngestionStep.initial,
          urlInput: trimmed,
        );
        return;
      }

      state = UrlIngestionState(
        step: UrlIngestionStep.failure,
        urlInput: trimmed,
        failure: failure,
      );
    } finally {
      if (identical(_activeCancelToken, cancelToken)) {
        _activeCancelToken = null;
      }
    }
  }

  void cancelActiveSubmission() {
    if (!state.isBusy) return;
    _operationId++;
    _activeCancelToken?.cancel('Cancelled by user');
    _activeCancelToken = null;

    state = UrlIngestionState(
      step: UrlIngestionStep.initial,
      urlInput: state.urlInput,
    );
  }

  void clearError() {
    if (state.failure == null) return;
    state = state.copyWith(
      step: UrlIngestionStep.initial,
      clearFailure: true,
    );
  }

  void reset() {
    _operationId++;
    _activeCancelToken?.cancel('Reset');
    _activeCancelToken = null;
    state = const UrlIngestionState.initial();
  }
}
