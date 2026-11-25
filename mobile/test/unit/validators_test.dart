import 'package:eduscroll/core/utils/validators.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('Validators', () {
    test('validates full name bounds and allows Unicode names', () {
      expect(Validators.fullName(''), isNotNull);
      expect(Validators.fullName('A'), isNotNull);
      expect(Validators.fullName('A' * 201), isNotNull);
      expect(Validators.fullName('Ada Lovelace'), isNull);
      expect(Validators.fullName('عباد الله'), isNull);
    });

    test('validates username rules matching backend policy', () {
      expect(Validators.userName(''), isNotNull);
      expect(Validators.userName('ab'), isNotNull);
      expect(Validators.userName('bad user!'), isNotNull);
      expect(Validators.userName('ada.lovelace_01-dev'), isNull);
    });

    test('validates email strictly rejecting malformed hosts', () {
      const invalidEmails = [
        '',
        'not-an-email',
        'user@localhost',
        'user@domain.',
        'user@.com',
        'user@domain..com',
        'user@-bad.com',
        'user@domain.c',
        'user..name@example.com',
        'user name@example.com',
      ];
      for (final bad in invalidEmails) {
        expect(Validators.email(bad), isNotNull, reason: 'Expected invalid: $bad');
      }

      expect(Validators.email('ada@example.com'), isNull);
      expect(Validators.email('  ada.lovelace+study@sub.edu.org  '), isNull);
    });

    test('validates registration password complexity and uniqueness', () {
      expect(Validators.registrationPassword(''), isNotNull);
      expect(Validators.registrationPassword('Aa1!bc'), isNotNull); // too short
      expect(Validators.registrationPassword('lowercase123!'), isNotNull); // no upper
      expect(Validators.registrationPassword('UPPERCASE123!'), isNotNull); // no lower
      expect(Validators.registrationPassword('NoDigitsHere!'), isNotNull); // no digit
      expect(Validators.registrationPassword('NoSpecial1234'), isNotNull); // no special
      expect(Validators.registrationPassword('Aa1!Aa1!'), isNotNull); // < 6 unique chars
      expect(Validators.registrationPassword('StrongPass123!'), isNull);
    });

    test('validates password confirmation', () {
      expect(Validators.confirmPassword('', 'StrongPass123!'), isNotNull);
      expect(
        Validators.confirmPassword('Different123!', 'StrongPass123!'),
        isNotNull,
      );
      expect(
        Validators.confirmPassword('StrongPass123!', 'StrongPass123!'),
        isNull,
      );
    });

    test('validates login password without enforcing registration complexity', () {
      expect(Validators.loginPassword(''), isNotNull);
      expect(Validators.loginPassword('   '), isNotNull);
      expect(Validators.loginPassword('a' * 129), isNotNull);
      expect(Validators.loginPassword('legacy'), isNull);
    });
  });
}
