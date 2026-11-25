import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../app/theme/app_spacing.dart';
import '../../../../core/error/app_failure.dart';
import '../../../../core/widgets/error_view.dart';
import '../../../../core/widgets/loading_indicator.dart';
import '../../../../core/widgets/secondary_button.dart';
import '../../../authentication/domain/entities/user_profile.dart';
import '../../../authentication/presentation/providers/auth_providers.dart';

class ProfileScreen extends ConsumerStatefulWidget {
  const ProfileScreen({super.key});

  @override
  ConsumerState<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends ConsumerState<ProfileScreen> {
  bool _isLoading = true;
  bool _isLoggingOut = false;
  AppFailure? _loadError;
  UserProfile? _profile;

  @override
  void initState() {
    super.initState();
    _profile = ref.read(authControllerProvider).user;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        unawaited(_loadProfile());
      }
    });
  }

  Future<void> _loadProfile() async {
    setState(() {
      _isLoading = true;
      _loadError = null;
    });

    try {
      final fetched =
          await ref.read(authControllerProvider.notifier).reloadCurrentUser();
      if (!mounted) return;
      setState(() {
        _profile = fetched;
        _isLoading = false;
      });
    } on AppFailure catch (failure) {
      if (!mounted) return;
      setState(() {
        _loadError = failure;
        _isLoading = false;
      });
    }
  }

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

  String _formatUtcDate(DateTime dateTime) {
    final local = dateTime.toLocal();
    final month = local.month.toString().padLeft(2, '0');
    final day = local.day.toString().padLeft(2, '0');
    return '${local.year}-$month-$day';
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;
    final profile = _profile ?? ref.watch(authControllerProvider).user;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Profile'),
        actions: [
          IconButton(
            key: const Key('profile_refresh_button'),
            tooltip: 'Refresh profile',
            onPressed: (_isLoading || _isLoggingOut) ? null : _loadProfile,
            icon: const Icon(Icons.refresh_rounded),
          ),
        ],
      ),
      body: SafeArea(
        child: _buildBody(context, theme, colors, profile),
      ),
    );
  }

  Widget _buildBody(
    BuildContext context,
    ThemeData theme,
    ColorScheme colors,
    UserProfile? profile,
  ) {
    if (_isLoading && profile == null) {
      return const LoadingIndicator(message: 'Loading your profile...');
    }

    if (_loadError != null && profile == null) {
      return ErrorView(
        title: 'Could not load profile',
        message: _loadError!.message,
        onRetry: _loadProfile,
        secondaryLabel: 'Sign out',
        onSecondaryAction: _confirmAndLogout,
      );
    }

    if (profile == null) {
      return ErrorView(
        title: 'Profile unavailable',
        message: 'We could not load your profile details.',
        onRetry: _loadProfile,
      );
    }

    return Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: AppSpacing.maxContentWidth),
        child: RefreshIndicator(
          onRefresh: _loadProfile,
          child: ListView(
            padding: AppSpacing.screenPadding,
            children: [
              if (_loadError != null) ...[
                InlineMessageBanner(
                  message: _loadError!.message,
                  isError: true,
                  onDismiss: () => setState(() => _loadError = null),
                ),
                const SizedBox(height: AppSpacing.md),
              ],
              Card(
                child: Padding(
                  padding: const EdgeInsets.all(AppSpacing.lg),
                  child: Row(
                    children: [
                      CircleAvatar(
                        radius: 28,
                        backgroundColor: colors.primaryContainer,
                        child: Text(
                          profile.initials,
                          style: theme.textTheme.titleLarge?.copyWith(
                            color: colors.onPrimaryContainer,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                      ),
                      const SizedBox(width: AppSpacing.md),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              profile.fullName,
                              style: theme.textTheme.titleLarge,
                            ),
                            const SizedBox(height: 2),
                            Text(
                              '@${profile.userName}',
                              style: theme.textTheme.bodyMedium,
                            ),
                          ],
                        ),
                      ),
                      if (_isLoading)
                        const SizedBox(
                          width: 20,
                          height: 20,
                          child: CircularProgressIndicator(strokeWidth: 2.2),
                        ),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: AppSpacing.md),
              Card(
                child: Column(
                  children: [
                    _ProfileDetailTile(
                      icon: Icons.badge_outlined,
                      label: 'Full name',
                      value: profile.fullName,
                    ),
                    const Divider(),
                    _ProfileDetailTile(
                      icon: Icons.alternate_email_rounded,
                      label: 'Username',
                      value: profile.userName,
                    ),
                    const Divider(),
                    _ProfileDetailTile(
                      icon: Icons.mail_outline_rounded,
                      label: 'Email',
                      value: profile.email,
                    ),
                    const Divider(),
                    _ProfileDetailTile(
                      icon: Icons.verified_outlined,
                      label: 'Account status',
                      value: profile.isActive ? 'Active' : 'Inactive',
                    ),
                    const Divider(),
                    _ProfileDetailTile(
                      icon: Icons.calendar_today_outlined,
                      label: 'Member since',
                      value: _formatUtcDate(profile.createdAt),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: AppSpacing.lg),
              SecondaryButton(
                key: const Key('profile_logout_button'),
                label: 'Sign out',
                icon: Icons.logout_rounded,
                isDestructive: true,
                isLoading: _isLoggingOut,
                onPressed: _confirmAndLogout,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _ProfileDetailTile extends StatelessWidget {
  const _ProfileDetailTile({
    required this.icon,
    required this.label,
    required this.value,
  });

  final IconData icon;
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return ListTile(
      leading: Icon(icon, size: 20, color: theme.colorScheme.onSurfaceVariant),
      title: Text(
        label,
        style: theme.textTheme.bodySmall?.copyWith(
          color: theme.colorScheme.onSurfaceVariant,
        ),
      ),
      subtitle: Text(
        value,
        style: theme.textTheme.bodyLarge?.copyWith(fontWeight: FontWeight.w500),
      ),
    );
  }
}
