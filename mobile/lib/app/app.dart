import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../features/authentication/presentation/controllers/auth_state.dart';
import '../features/authentication/presentation/providers/auth_providers.dart';
import 'router/app_router.dart';
import 'theme/app_theme.dart';

class EduScrollApp extends ConsumerStatefulWidget {
  const EduScrollApp({
    super.key,
    this.autoInitialize = true,
  });

  final bool autoInitialize;

  @override
  ConsumerState<EduScrollApp> createState() => _EduScrollAppState();
}

class _EduScrollAppState extends ConsumerState<EduScrollApp> {
  @override
  void initState() {
    super.initState();
    if (widget.autoInitialize) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted) return;
        final current = ref.read(authControllerProvider);
        if (current.status == AuthStatus.initializing) {
          unawaited(ref.read(authControllerProvider.notifier).initialize());
        }
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final router = ref.watch(appRouterProvider);

    return MaterialApp.router(
      title: 'EduScroll',
      debugShowCheckedModeBanner: false,
      theme: AppTheme.light,
      darkTheme: AppTheme.dark,
      themeMode: ThemeMode.dark,
      routerConfig: router,
    );
  }
}
