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

class _RegisterTestRepository implements AuthRepository {
  AppFailure? nextRegisterFailure;

  @override
  Future<UserProfile?> restoreSession() async => null;

  @override
  Future<AuthSession> login({
    required String email,
    required String password,
  }) async =>
      sampleSession();

  @override
  Future<UserProfile> register({
    required String fullName,
    required String userName,
    required String email,
    required String password,
  }) async {
    if (nextRegisterFailure != null) {
      final err = nextRegisterFailure!;
      nextRegisterFailure = null;
      throw err;
    }
    return sampleUser(fullName: fullName, userName: userName, email: email);
  }

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
  testWidgets('RegisterScreen validates confirmation, displays duplicate email error, and returns to Login on success', (tester) async {
    final repo = _RegisterTestRepository();

    await tester.pumpWidget(
      ProviderScope(
        overrides: [authRepositoryProvider.overrideWithValue(repo)],
        child: const EduScrollApp(),
      ),
    );
    await tester.pumpAndSettle();

    // Navigate from Login -> Register
    await tester.ensureVisible(find.byKey(const Key('login_go_to_register_button')));
    await tester.tap(find.byKey(const Key('login_go_to_register_button')));
    await tester.pumpAndSettle();

    expect(find.text('Get started'), findsOneWidget);

    // Enter mismatched confirm password
    await tester.enterText(
      find.byKey(const Key('register_full_name_field')),
      'Grace Hopper',
    );
    await tester.enterText(
      find.byKey(const Key('register_username_field')),
      'grace.hopper',
    );
    await tester.enterText(
      find.byKey(const Key('register_email_field')),
      'grace@example.com',
    );
    await tester.enterText(
      find.byKey(const Key('register_password_field')),
      'StrongPassword123!',
    );
    await tester.enterText(
      find.byKey(const Key('register_confirm_password_field')),
      'Mismatch123!',
    );

    await tester.ensureVisible(find.byKey(const Key('register_submit_button')));
    await tester.tap(find.byKey(const Key('register_submit_button')));
    await tester.pump();

    expect(find.text('Passwords do not match.'), findsOneWidget);

    // Fix confirmation, simulate backend 409 duplicate email
    await tester.enterText(
      find.byKey(const Key('register_confirm_password_field')),
      'StrongPassword123!',
    );
    repo.nextRegisterFailure = const AppFailure(
      type: AppFailureType.conflict,
      code: 'DUPLICATE_EMAIL',
      statusCode: 409,
      message: 'An account with this email already exists.',
      fieldErrors: {
        'email': ['An account with this email already exists.'],
      },
    );

    await tester.ensureVisible(find.byKey(const Key('register_submit_button')));
    await tester.tap(find.byKey(const Key('register_submit_button')));
    await tester.pumpAndSettle();

    expect(find.text('An account with this email already exists.'), findsAtLeastNWidgets(1));

    // Change email and succeed -> redirects to Login with success banner & prefilled email
    await tester.enterText(
      find.byKey(const Key('register_email_field')),
      'grace.new@example.com',
    );
    await tester.ensureVisible(find.byKey(const Key('register_submit_button')));
    await tester.tap(find.byKey(const Key('register_submit_button')));
    await tester.pumpAndSettle();

    expect(find.text('Welcome back'), findsOneWidget);
    expect(find.textContaining('grace.new@example.com'), findsAtLeastNWidgets(1));
  });
}
