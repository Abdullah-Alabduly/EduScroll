/// Client-side input validators aligned with EduScroll.API AuthRequestValidator.
abstract final class Validators {
  static const int minFullNameLength = 2;
  static const int maxFullNameLength = 200;
  static const int minUserNameLength = 3;
  static const int maxUserNameLength = 50;
  static const int maxEmailLength = 256;
  static const int minPasswordLength = 8;
  static const int maxPasswordLength = 128;
  static const int minUniquePasswordChars = 6;

  static final RegExp _userNamePattern = RegExp(r'^[a-zA-Z0-9._-]+$');
  static final RegExp _upperCasePattern = RegExp(r'[A-Z]');
  static final RegExp _lowerCasePattern = RegExp(r'[a-z]');
  static final RegExp _digitPattern = RegExp(r'[0-9]');
  static final RegExp _nonAlphanumericPattern = RegExp(r'[^a-zA-Z0-9]');

  static String? fullName(String? value) {
    final trimmed = (value ?? '').trim();
    if (trimmed.isEmpty) {
      return 'Full name is required.';
    }
    if (trimmed.length < minFullNameLength ||
        trimmed.length > maxFullNameLength) {
      return 'Full name must be between $minFullNameLength and $maxFullNameLength characters.';
    }
    return null;
  }

  static String? userName(String? value) {
    final trimmed = (value ?? '').trim();
    if (trimmed.isEmpty) {
      return 'Username is required.';
    }
    if (trimmed.length < minUserNameLength ||
        trimmed.length > maxUserNameLength) {
      return 'Username must be between $minUserNameLength and $maxUserNameLength characters.';
    }
    if (!_userNamePattern.hasMatch(trimmed)) {
      return "Username may only contain letters, numbers, '.', '_', and '-'.";
    }
    return null;
  }

  static String? email(String? value) {
    final trimmed = (value ?? '').trim();
    if (trimmed.isEmpty) {
      return 'Email is required.';
    }
    if (trimmed.length > maxEmailLength) {
      return 'Email must not exceed $maxEmailLength characters.';
    }
    if (trimmed.contains(RegExp(r'\s')) || trimmed.contains('..')) {
      return 'Please enter a valid email address.';
    }

    final parts = trimmed.split('@');
    if (parts.length != 2 || parts[0].isEmpty || parts[1].isEmpty) {
      return 'Please enter a valid email address.';
    }

    final localPart = parts[0];
    final host = parts[1];
    if (localPart.startsWith('.') || localPart.endsWith('.')) {
      return 'Please enter a valid email address.';
    }
    if (host.startsWith('.') || host.endsWith('.')) {
      return 'Please enter a valid email address.';
    }

    final hostLabels = host.split('.');
    if (hostLabels.length < 2 || hostLabels.last.length < 2) {
      return 'Please enter a valid email address.';
    }
    for (final label in hostLabels) {
      if (label.isEmpty || label.startsWith('-') || label.endsWith('-')) {
        return 'Please enter a valid email address.';
      }
    }

    return null;
  }

  static String? loginPassword(String? value) {
    final raw = value ?? '';
    if (raw.trim().isEmpty) {
      return 'Password is required.';
    }
    if (raw.length > maxPasswordLength) {
      return 'Password must not exceed $maxPasswordLength characters.';
    }
    return null;
  }

  static String? registrationPassword(String? value) {
    final raw = value ?? '';
    if (raw.trim().isEmpty) {
      return 'Password is required.';
    }
    if (raw.length < minPasswordLength || raw.length > maxPasswordLength) {
      return 'Password must be between $minPasswordLength and $maxPasswordLength characters.';
    }
    if (!_upperCasePattern.hasMatch(raw)) {
      return 'Password must include at least one uppercase letter.';
    }
    if (!_lowerCasePattern.hasMatch(raw)) {
      return 'Password must include at least one lowercase letter.';
    }
    if (!_digitPattern.hasMatch(raw)) {
      return 'Password must include at least one number.';
    }
    if (!_nonAlphanumericPattern.hasMatch(raw)) {
      return 'Password must include at least one special character.';
    }
    if (raw.runes.toSet().length < minUniquePasswordChars) {
      return 'Password must contain at least $minUniquePasswordChars unique characters.';
    }
    return null;
  }

  static String? confirmPassword(String? value, String password) {
    if ((value ?? '').isEmpty) {
      return 'Please confirm your password.';
    }
    if (value != password) {
      return 'Passwords do not match.';
    }
    return null;
  }
}
