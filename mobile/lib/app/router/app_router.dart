import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../features/authentication/presentation/controllers/auth_state.dart';
import '../../features/authentication/presentation/providers/auth_providers.dart';
import '../../features/authentication/presentation/screens/login_screen.dart';
import '../../features/authentication/presentation/screens/register_screen.dart';
import '../../features/authentication/presentation/screens/splash_screen.dart';
import '../../features/home/presentation/screens/home_screen.dart';
import '../../features/profile/presentation/screens/profile_screen.dart';
import '../../features/sources/presentation/screens/add_source_screen.dart';
import '../../features/sources/presentation/screens/my_sources_screen.dart';
import '../../features/sources/presentation/screens/pdf_import_screen.dart';
import '../../features/sources/presentation/screens/source_detail_screen.dart';
import '../../features/sources/presentation/screens/url_import_screen.dart';
import 'main_shell_screen.dart';

abstract final class AppRoutes {
  static const String splash = '/splash';
  static const String login = '/login';
  static const String register = '/register';
  static const String home = '/home';
  static const String addSource = '/add-source';
  static const String addSourcePdf = '/add-source/pdf';
  static const String addSourceUrl = '/add-source/url';
  static const String sources = '/sources';
  static String sourceDetail(String id) => '/sources/$id';
  static const String profile = '/profile';
}

/// Pure, unit-testable centralized route guard function.
String? resolveAuthRedirect({
  required AuthState authState,
  required String currentLocation,
}) {
  final isSplash = currentLocation == AppRoutes.splash;
  final isPublicAuthRoute =
      currentLocation == AppRoutes.login ||
      currentLocation == AppRoutes.register;

  if (authState.isStartupPhase) {
    return isSplash ? null : AppRoutes.splash;
  }

  if (authState.isAuthenticated) {
    if (isSplash || isPublicAuthRoute) {
      return AppRoutes.home;
    }
    return null;
  }

  // Unauthenticated or authenticating
  if (isPublicAuthRoute) {
    return null;
  }
  return AppRoutes.login;
}

final appRouterProvider = Provider<GoRouter>((ref) {
  final refreshNotifier = _AuthRouterRefreshNotifier(ref);
  final rootNavigatorKey = GlobalKey<NavigatorState>();

  final router = GoRouter(
    navigatorKey: rootNavigatorKey,
    initialLocation: AppRoutes.splash,
    refreshListenable: refreshNotifier,
    redirect: (context, state) {
      final authState = ref.read(authControllerProvider);
      return resolveAuthRedirect(
        authState: authState,
        currentLocation: state.matchedLocation,
      );
    },
    routes: [
      GoRoute(
        path: AppRoutes.splash,
        builder: (context, state) => const SplashScreen(),
      ),
      GoRoute(
        path: AppRoutes.login,
        builder: (context, state) => const LoginScreen(),
      ),
      GoRoute(
        path: AppRoutes.register,
        builder: (context, state) => const RegisterScreen(),
      ),
      StatefulShellRoute.indexedStack(
        builder: (context, state, navigationShell) => MainShellScreen(
          navigationShell: navigationShell,
        ),
        branches: [
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: AppRoutes.home,
                builder: (context, state) => const HomeScreen(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: AppRoutes.addSource,
                builder: (context, state) => const AddSourceScreen(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: AppRoutes.sources,
                builder: (context, state) => const MySourcesScreen(),
              ),
            ],
          ),
        ],
      ),
      GoRoute(
        parentNavigatorKey: rootNavigatorKey,
        path: AppRoutes.addSourcePdf,
        builder: (context, state) => const PdfImportScreen(),
      ),
      GoRoute(
        parentNavigatorKey: rootNavigatorKey,
        path: AppRoutes.addSourceUrl,
        builder: (context, state) => const UrlImportScreen(),
      ),
      GoRoute(
        parentNavigatorKey: rootNavigatorKey,
        path: '${AppRoutes.sources}/:id',
        builder: (context, state) {
          final id = state.pathParameters['id'] ?? '';
          return SourceDetailScreen(sourceId: id);
        },
      ),
      GoRoute(
        parentNavigatorKey: rootNavigatorKey,
        path: AppRoutes.profile,
        builder: (context, state) => const ProfileScreen(),
      ),
    ],
  );

  ref.onDispose(() {
    refreshNotifier.dispose();
    router.dispose();
  });

  return router;
});

class _AuthRouterRefreshNotifier extends ChangeNotifier {
  _AuthRouterRefreshNotifier(Ref ref) {
    _subscription = ref.listen<AuthState>(
      authControllerProvider,
      (previous, next) {
        if (previous?.status != next.status) {
          notifyListeners();
        }
      },
    );
  }

  late final ProviderSubscription<AuthState> _subscription;

  @override
  void dispose() {
    _subscription.close();
    super.dispose();
  }
}
