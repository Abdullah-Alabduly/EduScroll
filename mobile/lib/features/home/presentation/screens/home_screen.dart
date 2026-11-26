import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../../app/router/app_router.dart';
import '../../../../app/theme/app_spacing.dart';
import '../../../../core/widgets/app_logo.dart';
import '../../../../core/widgets/empty_state.dart';
import '../../../../core/widgets/secondary_button.dart';
import '../../../authentication/presentation/providers/auth_providers.dart';

class HomeScreen extends ConsumerStatefulWidget {
  const HomeScreen({super.key});

  @override
  ConsumerState<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends ConsumerState<HomeScreen> {
  bool _isLoggingOut = false;

  Future<void> _confirmAndLogout() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Sign out'),
        content: const Text('Are you sure you want to sign out of EduScroll?'),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.of(dialogContext).pop(true),
            child: const Text('Sign out'),
          ),
        ],
      ),
    );

    if (confirmed != true || !mounted) return;

    setState(() => _isLoggingOut = true);
    try {
      await ref.read(authControllerProvider.notifier).logout();
    } finally {
      if (mounted) {
        setState(() => _isLoggingOut = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;
    final user = ref.watch(authControllerProvider).user;

    return Scaffold(
      appBar: AppBar(
        title: const AppLogo(compact: true),
        actions: [
          IconButton(
            key: const Key('home_profile_button'),
            tooltip: 'Profile',
            onPressed: _isLoggingOut
                ? null
                : () => context.push(AppRoutes.profile),
            icon: CircleAvatar(
              radius: 16,
              backgroundColor: colors.primaryContainer,
              child: Text(
                user?.initials ?? '?',
                style: theme.textTheme.labelMedium?.copyWith(
                  color: colors.onPrimaryContainer,
                  fontWeight: FontWeight.w700,
                ),
              ),
            ),
          ),
          IconButton(
            key: const Key('home_logout_icon_button'),
            tooltip: 'Sign out',
            onPressed: _isLoggingOut ? null : _confirmAndLogout,
            icon: const Icon(Icons.logout_rounded),
          ),
          const SizedBox(width: AppSpacing.xs),
        ],
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
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(AppSpacing.lg),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            Container(
                              padding: const EdgeInsets.symmetric(
                                horizontal: AppSpacing.sm,
                                vertical: AppSpacing.xxs,
                              ),
                              decoration: BoxDecoration(
                                color: colors.secondaryContainer,
                                borderRadius: BorderRadius.circular(999),
                              ),
                              child: Row(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  Icon(
                                    Icons.verified_user_outlined,
                                    size: 14,
                                    color: colors.onSecondaryContainer,
                                  ),
                                  const SizedBox(width: AppSpacing.xxs),
                                  Text(
                                    'Signed in',
                                    style: theme.textTheme.labelSmall?.copyWith(
                                      color: colors.onSecondaryContainer,
                                      fontWeight: FontWeight.w600,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ],
                        ),
                        const SizedBox(height: AppSpacing.md),
                        Text(
                          user != null
                              ? 'Welcome, ${user.fullName}'
                              : 'Welcome to EduScroll',
                          style: theme.textTheme.headlineSmall,
                        ),
                        if (user != null) ...[
                          const SizedBox(height: AppSpacing.xxs),
                          Text(
                            '@${user.userName} · ${user.email}',
                            style: theme.textTheme.bodyMedium,
                          ),
                        ],
                        const SizedBox(height: AppSpacing.md),
                        Wrap(
                          spacing: AppSpacing.sm,
                          runSpacing: AppSpacing.sm,
                          children: [
                            FilledButton.icon(
                              key: const Key('home_add_source_button'),
                              onPressed: _isLoggingOut
                                  ? null
                                  : () => context.go(AppRoutes.addSource),
                              icon: const Icon(Icons.add_circle_outline_rounded, size: 18),
                              label: const Text('Add source'),
                            ),
                            FilledButton.tonalIcon(
                              key: const Key('home_my_sources_button'),
                              onPressed: _isLoggingOut
                                  ? null
                                  : () => context.go(AppRoutes.sources),
                              icon: const Icon(Icons.library_books_outlined, size: 18),
                              label: const Text('My sources'),
                            ),
                            FilledButton.tonalIcon(
                              key: const Key('home_open_profile_button'),
                              onPressed: _isLoggingOut
                                  ? null
                                  : () => context.push(AppRoutes.profile),
                              icon: const Icon(Icons.person_outline_rounded, size: 18),
                              label: const Text('View profile'),
                            ),
                            OutlinedButton.icon(
                              key: const Key('home_logout_button'),
                              onPressed:
                                  _isLoggingOut ? null : _confirmAndLogout,
                              icon: const Icon(Icons.logout_rounded, size: 18),
                              label: Text(
                                _isLoggingOut ? 'Signing out...' : 'Sign out',
                              ),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                ),
                const SizedBox(height: AppSpacing.lg),
                EmptyState(
                  icon: Icons.auto_stories_outlined,
                  badgeText: 'Sources Ready',
                  title: 'Educational discovery arrives next',
                  description:
                      'Import PDF documents and public web articles using the Add Source tab. '
                      'In upcoming sprints, your saved sources will power personalized short-video discovery.',
                  actionLabel: 'Import a PDF or article',
                  onAction: _isLoggingOut
                      ? null
                      : () => context.go(AppRoutes.addSource),
                ),
                if (_isLoggingOut) ...[
                  const SizedBox(height: AppSpacing.lg),
                  const SecondaryButton(
                    label: 'Signing out...',
                    onPressed: null,
                    isLoading: true,
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
