import 'package:flutter/foundation.dart';

import '../../../../core/error/app_failure.dart';
import '../../domain/entities/source_detail.dart';

enum UrlIngestionStep {
  initial,
  validating,
  submitting,
  success,
  failure,
}

@immutable
class UrlIngestionState {
  const UrlIngestionState({
    required this.step,
    this.urlInput = '',
    this.createdSource,
    this.failure,
  });

  const UrlIngestionState.initial() : this(step: UrlIngestionStep.initial);

  final UrlIngestionStep step;
  final String urlInput;
  final SourceDetail? createdSource;
  final AppFailure? failure;

  bool get isBusy =>
      step == UrlIngestionStep.validating ||
      step == UrlIngestionStep.submitting;

  UrlIngestionState copyWith({
    UrlIngestionStep? step,
    String? urlInput,
    SourceDetail? createdSource,
    AppFailure? failure,
    bool clearCreatedSource = false,
    bool clearFailure = false,
  }) {
    return UrlIngestionState(
      step: step ?? this.step,
      urlInput: urlInput ?? this.urlInput,
      createdSource:
          clearCreatedSource ? null : (createdSource ?? this.createdSource),
      failure: clearFailure ? null : (failure ?? this.failure),
    );
  }
}
