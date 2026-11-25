import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../app/theme/app_spacing.dart';
import '../../../../core/widgets/app_logo.dart';
import '../../../../core/widgets/error_view.dart';
import '../../../../core/widgets/loading_indicator.dart';
import '../controllers/auth_state.dart';
import '../providers/auth_providers.dart';

class SplashScreen extends ConsumerWidget {
  const SplashScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final authState = ref.watch(authControllerProvider);

    return Scaffold(
      body: SafeArea(
        child: authState.status == AuthStatus.error
            ? ErrorView(
                title: 'Unable to reach EduScroll',
                message: authState.errorMessage ??
                    'We could not verify your session because the server is currently unavailable.',
                retryLabel: 'Retry connection',
                onRetry: () {
                  unawaited(
                    ref.read(authControllerProvider.notifier).initialize(),
                  );
                },
                secondaryLabel: 'Sign out on this device',
                onSecondaryAction: () {
                  unawaited(
                    ref.read(authControllerProvider.notifier).signOutLocally(),
                  );
                },
              )
            : const Center(
                child: Padding(
                  padding: AppSpacing.screenPadding,
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      AppLogo(
                        subtitle: 'Focused educational short-video discovery',
                      ),
                      SizedBox(height: AppSpacing.xl),
                      LoadingIndicator(message: 'Preparing your workspace...'),
                    ],
                  ),
                ),
              ),
      ),
    );
  }
}
