import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../../app/router/app_router.dart';
import '../../../../app/theme/app_spacing.dart';
import '../../../../core/error/app_failure.dart';
import '../../../../core/utils/validators.dart';
import '../../../../core/widgets/app_logo.dart';
import '../../../../core/widgets/app_text_field.dart';
import '../../../../core/widgets/error_view.dart';
import '../../../../core/widgets/password_field.dart';
import '../../../../core/widgets/primary_button.dart';
import '../controllers/auth_state.dart';
import '../providers/auth_providers.dart';
import '../widgets/auth_scaffold.dart';

class RegisterScreen extends ConsumerStatefulWidget {
  const RegisterScreen({super.key});

  @override
  ConsumerState<RegisterScreen> createState() => _RegisterScreenState();
}

class _RegisterScreenState extends ConsumerState<RegisterScreen> {
  final _formKey = GlobalKey<FormState>();
  final _fullNameController = TextEditingController();
  final _userNameController = TextEditingController();
  final _emailController = TextEditingController();
  final _passwordController = TextEditingController();
  final _confirmPasswordController = TextEditingController();

  final _fullNameFocus = FocusNode();
  final _userNameFocus = FocusNode();
  final _emailFocus = FocusNode();
  final _passwordFocus = FocusNode();
  final _confirmPasswordFocus = FocusNode();

  bool _isSubmitting = false;
  String? _submitError;
  Map<String, List<String>> _serverFieldErrors = const {};

  @override
  void dispose() {
    _fullNameController.dispose();
    _userNameController.dispose();
    _emailController.dispose();
    _passwordController.dispose();
    _confirmPasswordController.dispose();
    _fullNameFocus.dispose();
    _userNameFocus.dispose();
    _emailFocus.dispose();
    _passwordFocus.dispose();
    _confirmPasswordFocus.dispose();
    super.dispose();
  }

  void _clearFieldError(String field) {
    if (_serverFieldErrors.containsKey(field) || _submitError != null) {
      setState(() {
        _submitError = null;
        final updated = Map<String, List<String>>.from(_serverFieldErrors);
        updated.remove(field);
        _serverFieldErrors = updated;
      });
    }
  }

  String? _serverErrorFor(String field) {
    final lower = field.toLowerCase();
    for (final entry in _serverFieldErrors.entries) {
      if (entry.key.toLowerCase() == lower && entry.value.isNotEmpty) {
        return entry.value.first;
      }
    }
    return null;
  }

  Future<void> _submit() async {
    FocusScope.of(context).unfocus();
    setState(() {
      _submitError = null;
      _serverFieldErrors = const {};
    });

    if (!(_formKey.currentState?.validate() ?? false)) {
      return;
    }

    setState(() => _isSubmitting = true);
    try {
      await ref.read(authControllerProvider.notifier).register(
            fullName: _fullNameController.text.trim(),
            userName: _userNameController.text.trim(),
            email: _emailController.text.trim(),
            password: _passwordController.text,
          );
      if (!mounted) return;
      context.go(AppRoutes.login);
    } on AppFailure catch (failure) {
      if (!mounted) return;
      setState(() {
        _submitError = failure.message;
        _serverFieldErrors = failure.fieldErrors;
      });
    } finally {
      if (mounted) {
        setState(() => _isSubmitting = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final authState = ref.watch(authControllerProvider);
    final isBusy = _isSubmitting || authState.status == AuthStatus.authenticating;

    return AuthScaffold(
      child: AutofillGroup(
        child: Form(
          key: _formKey,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const AppLogo(
                subtitle: 'Create your EduScroll learner account',
              ),
              const SizedBox(height: AppSpacing.lg),
              Card(
                child: Padding(
                  padding: const EdgeInsets.all(AppSpacing.lg),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text('Get started', style: theme.textTheme.headlineSmall),
                      const SizedBox(height: AppSpacing.xxs),
                      Text(
                        'Register with your details, then sign in to begin.',
                        style: theme.textTheme.bodyMedium,
                      ),
                      if (_submitError != null) ...[
                        const SizedBox(height: AppSpacing.md),
                        InlineMessageBanner(
                          message: _submitError!,
                          isError: true,
                          onDismiss: () => setState(() => _submitError = null),
                        ),
                      ],
                      const SizedBox(height: AppSpacing.lg),
                      AppTextField(
                        key: const Key('register_full_name_field'),
                        controller: _fullNameController,
                        focusNode: _fullNameFocus,
                        label: 'Full name',
                        hintText: 'Ada Lovelace',
                        prefixIcon: Icons.person_outline_rounded,
                        textCapitalization: TextCapitalization.words,
                        textInputAction: TextInputAction.next,
                        autofillHints: const [AutofillHints.name],
                        enabled: !isBusy,
                        errorText: _serverErrorFor('fullName'),
                        validator: Validators.fullName,
                        onChanged: (_) => _clearFieldError('fullName'),
                        onFieldSubmitted: (_) => _userNameFocus.requestFocus(),
                      ),
                      const SizedBox(height: AppSpacing.md),
                      AppTextField(
                        key: const Key('register_username_field'),
                        controller: _userNameController,
                        focusNode: _userNameFocus,
                        label: 'Username',
                        hintText: 'ada.lovelace',
                        helperText: "3–50 characters: letters, numbers, '.', '_', '-'",
                        prefixIcon: Icons.alternate_email_rounded,
                        textInputAction: TextInputAction.next,
                        autofillHints: const [AutofillHints.newUsername],
                        enabled: !isBusy,
                        errorText: _serverErrorFor('userName'),
                        validator: Validators.userName,
                        onChanged: (_) => _clearFieldError('userName'),
                        onFieldSubmitted: (_) => _emailFocus.requestFocus(),
                      ),
                      const SizedBox(height: AppSpacing.md),
                      AppTextField(
                        key: const Key('register_email_field'),
                        controller: _emailController,
                        focusNode: _emailFocus,
                        label: 'Email',
                        hintText: 'you@example.com',
                        prefixIcon: Icons.mail_outline_rounded,
                        keyboardType: TextInputType.emailAddress,
                        textInputAction: TextInputAction.next,
                        autofillHints: const [AutofillHints.email],
                        enabled: !isBusy,
                        errorText: _serverErrorFor('email'),
                        validator: Validators.email,
                        onChanged: (_) => _clearFieldError('email'),
                        onFieldSubmitted: (_) => _passwordFocus.requestFocus(),
                      ),
                      const SizedBox(height: AppSpacing.md),
                      PasswordField(
                        key: const Key('register_password_field'),
                        controller: _passwordController,
                        focusNode: _passwordFocus,
                        label: 'Password',
                        hintText: 'Create a strong password',
                        helperText:
                            'At least 8 chars with upper, lower, number, special & 6 unique chars.',
                        textInputAction: TextInputAction.next,
                        autofillHints: const [AutofillHints.newPassword],
                        enabled: !isBusy,
                        errorText: _serverErrorFor('password'),
                        validator: Validators.registrationPassword,
                        onChanged: (_) => _clearFieldError('password'),
                        onFieldSubmitted: (_) =>
                            _confirmPasswordFocus.requestFocus(),
                      ),
                      const SizedBox(height: AppSpacing.md),
                      PasswordField(
                        key: const Key('register_confirm_password_field'),
                        controller: _confirmPasswordController,
                        focusNode: _confirmPasswordFocus,
                        label: 'Confirm password',
                        hintText: 'Re-enter your password',
                        textInputAction: TextInputAction.done,
                        autofillHints: const [AutofillHints.newPassword],
                        enabled: !isBusy,
                        validator: (value) => Validators.confirmPassword(
                          value,
                          _passwordController.text,
                        ),
                        onFieldSubmitted: (_) => _submit(),
                      ),
                      const SizedBox(height: AppSpacing.lg),
                      PrimaryButton(
                        key: const Key('register_submit_button'),
                        label: 'Create account',
                        isLoading: isBusy,
                        onPressed: _submit,
                      ),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: AppSpacing.md),
              Wrap(
                alignment: WrapAlignment.center,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  Text(
                    'Already have an account?',
                    style: theme.textTheme.bodyMedium,
                  ),
                  TextButton(
                    key: const Key('register_go_to_login_button'),
                    onPressed: isBusy
                        ? null
                        : () {
                            ref
                                .read(authControllerProvider.notifier)
                                .clearNotices();
                            context.go(AppRoutes.login);
                          },
                    child: const Text('Sign in'),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}
