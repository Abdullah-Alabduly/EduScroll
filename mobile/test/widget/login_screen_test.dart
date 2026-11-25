import 'dart:async';

import 'package:eduscroll/app/app.dart';
import 'package:eduscroll/core/error/app_failure.dart';
import 'package:eduscroll/features/authentication/domain/entities/auth_session.dart';
import 'package:eduscroll/features/authentication/domain/entities/user_profile.dart';
import 'package:eduscroll/features/authentication/domain/repositories/auth_repository.dart';
import 'package:eduscroll/features/authentication/presentation/providers/auth_providers.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

import '../helpers/test_fakes.dart';

class _WidgetAuthRepository implements AuthRepository {
  Completer<AuthSession>? pendingLogin;
  AppFailure? nextLoginFailure;

  @override
  Future<UserProfile?> restoreSession() async => null;

  @override
  Future<AuthSession> login({
    required String email,
    required String password,
  }) async {
    if (pendingLogin != null) {
      return pendingLogin!.future;
    }
    if (nextLoginFailure != null) {
      throw nextLoginFailure!;
    }
    return sampleSession();
  }

  @override
  Future<UserProfile> register({
    required String fullName,
    required String userName,
    required String email,
    required String password,
  }) async =>
      sampleUser(fullName: fullName, userName: userName, email: email);

  @override
  Future<AuthSession> refreshSession() async => sampleSession();

  @override
  Future<UserProfile> getCurrentUser() async => sampleUser();

  @override
  Future<LogoutResult> logout() async => const LogoutResult(serverRevoked: true);

  @override
  Future<void> clearLocalSession() async {}
}

void main() {
  testWidgets('LoginScreen validates inputs, toggles password visibility, shows loading & errors, and navigates to Register', (tester) async {
    final repo = _WidgetAuthRepository();

    await tester.pumpWidget(
      ProviderScope(
        overrides: [authRepositoryProvider.overrideWithValue(repo)],
        child: const EduScrollApp(),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Welcome back'), findsOneWidget);
    expect(find.byKey(const Key('login_email_field')), findsOneWidget);
    expect(find.byKey(const Key('login_password_field')), findsOneWidget);

    // 1. Validation on empty submission
    await tester.tap(find.byKey(const Key('login_submit_button')));
    await tester.pump();
    expect(find.text('Email is required.'), findsOneWidget);
    expect(find.text('Password is required.'), findsOneWidget);

    // 2. Password visibility toggle
    final passwordEditableBefore = tester.widget<EditableText>(
      find.descendant(
        of: find.byKey(const Key('login_password_field')),
        matching: find.byType(EditableText),
      ),
    );
    expect(passwordEditableBefore.obscureText, isTrue);

    await tester.tap(find.byTooltip('Show password'));
    await tester.pump();

    final passwordEditableAfter = tester.widget<EditableText>(
      find.descendant(
        of: find.byKey(const Key('login_password_field')),
        matching: find.byType(EditableText),
      ),
    );
    expect(passwordEditableAfter.obscureText, isFalse);

    // 3. Loading indicator while login is in flight, then error banner on failure
    repo.pendingLogin = Completer<AuthSession>();

    await tester.enterText(
      find.byKey(const Key('login_email_field')),
      'ada@example.com',
    );
    await tester.enterText(
      find.byKey(const Key('login_password_field')),
      'WrongPass123!',
    );
    await tester.tap(find.byKey(const Key('login_submit_button')));
    await tester.pump();

    expect(find.byType(CircularProgressIndicator), findsOneWidget);

    repo.pendingLogin!.completeError(
      const AppFailure(
        type: AppFailureType.unauthorized,
        code: 'INVALID_CREDENTIALS',
        statusCode: 401,
        message: 'Invalid email or password.',
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Invalid email or password.'), findsOneWidget);

    // 4. Navigate to Register screen
    await tester.ensureVisible(find.byKey(const Key('login_go_to_register_button')));
    await tester.tap(find.byKey(const Key('login_go_to_register_button')));
    await tester.pumpAndSettle();

    expect(find.text('Get started'), findsOneWidget);
  });
}
