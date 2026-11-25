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

class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({super.key});

  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends ConsumerState<LoginScreen> {
  final _formKey = GlobalKey<FormState>();
  final _emailController = TextEditingController();
  final _passwordController = TextEditingController();
  final _emailFocusNode = FocusNode();
  final _passwordFocusNode = FocusNode();

  bool _isSubmitting = false;
  String? _submitError;
  Map<String, List<String>> _serverFieldErrors = const {};

  @override
  void initState() {
    super.initState();
    final initialEmail = ref.read(authControllerProvider).prefilledEmail;
    if (initialEmail != null && initialEmail.isNotEmpty) {
      _emailController.text = initialEmail;
    }
  }

  @override
  void dispose() {
    _emailController.dispose();
    _passwordController.dispose();
    _emailFocusNode.dispose();
    _passwordFocusNode.dispose();
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
      await ref.read(authControllerProvider.notifier).login(
            email: _emailController.text.trim(),
            password: _passwordController.text,
          );
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

    final bannerError = _submitError ?? authState.errorMessage;
    final bannerInfo = bannerError == null ? authState.infoMessage : null;

    return AuthScaffold(
      child: AutofillGroup(
        child: Form(
          key: _formKey,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const AppLogo(
                subtitle: 'Sign in to continue your learning journey',
              ),
              const SizedBox(height: AppSpacing.xl),
              Card(
                child: Padding(
                  padding: const EdgeInsets.all(AppSpacing.lg),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text('Welcome back', style: theme.textTheme.headlineSmall),
                      const SizedBox(height: AppSpacing.xxs),
                      Text(
                        'Enter your email and password to access EduScroll.',
                        style: theme.textTheme.bodyMedium,
                      ),
                      if (bannerError != null) ...[
                        const SizedBox(height: AppSpacing.md),
                        InlineMessageBanner(
                          message: bannerError,
                          isError: true,
                          onDismiss: () {
                            setState(() => _submitError = null);
                            ref
                                .read(authControllerProvider.notifier)
                                .clearNotices();
                          },
                        ),
                      ],
                      if (bannerInfo != null) ...[
                        const SizedBox(height: AppSpacing.md),
                        InlineMessageBanner(
                          message: bannerInfo,
                          isError: false,
                          onDismiss: () {
                            ref
                                .read(authControllerProvider.notifier)
                                .clearNotices();
                          },
                        ),
                      ],
                      const SizedBox(height: AppSpacing.lg),
                      AppTextField(
                        key: const Key('login_email_field'),
                        controller: _emailController,
                        focusNode: _emailFocusNode,
                        label: 'Email',
                        hintText: 'you@example.com',
                        prefixIcon: Icons.mail_outline_rounded,
                        keyboardType: TextInputType.emailAddress,
                        textInputAction: TextInputAction.next,
                        autofillHints: const [AutofillHints.email],
                        enabled: !isBusy,
                        errorText: _serverFieldErrors['email']?.firstOrNull,
                        validator: Validators.email,
                        onChanged: (_) => _clearFieldError('email'),
                        onFieldSubmitted: (_) =>
                            _passwordFocusNode.requestFocus(),
                      ),
                      const SizedBox(height: AppSpacing.md),
                      PasswordField(
                        key: const Key('login_password_field'),
                        controller: _passwordController,
                        focusNode: _passwordFocusNode,
                        label: 'Password',
                        hintText: 'Enter your password',
                        textInputAction: TextInputAction.done,
                        autofillHints: const [AutofillHints.password],
                        enabled: !isBusy,
                        errorText: _serverFieldErrors['password']?.firstOrNull,
                        validator: Validators.loginPassword,
                        onChanged: (_) => _clearFieldError('password'),
                        onFieldSubmitted: (_) => _submit(),
                      ),
                      const SizedBox(height: AppSpacing.lg),
                      PrimaryButton(
                        key: const Key('login_submit_button'),
                        label: 'Sign in',
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
                    "Don't have an account?",
                    style: theme.textTheme.bodyMedium,
                  ),
                  TextButton(
                    key: const Key('login_go_to_register_button'),
                    onPressed: isBusy
                        ? null
                        : () {
                            ref
                                .read(authControllerProvider.notifier)
                                .clearNotices();
                            context.go(AppRoutes.register);
                          },
                    child: const Text('Create one'),
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
