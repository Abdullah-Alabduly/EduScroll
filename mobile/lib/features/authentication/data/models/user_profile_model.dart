import '../../domain/entities/user_profile.dart';

abstract final class UserProfileModel {
  static UserProfile fromJson(Map<String, dynamic> json) {
    final id = _requireString(json, 'id');
    final fullName = _requireString(json, 'fullName');
    final userName = _requireString(json, 'userName');
    final email = _requireString(json, 'email');
    final createdAtRaw = _requireString(json, 'createdAt');
    final updatedAtRaw = _requireString(json, 'updatedAt');
    final isActive = json['isActive'];

    if (isActive is! bool) {
      throw const FormatException('Missing or invalid boolean field: isActive');
    }

    return UserProfile(
      id: id,
      fullName: fullName,
      userName: userName,
      email: email,
      createdAt: DateTime.parse(createdAtRaw).toUtc(),
      updatedAt: DateTime.parse(updatedAtRaw).toUtc(),
      isActive: isActive,
    );
  }

  static String _requireString(Map<String, dynamic> json, String key) {
    final value = json[key];
    if (value is! String || value.trim().isEmpty) {
      throw FormatException('Missing or invalid string field: $key');
    }
    return value;
  }
}
