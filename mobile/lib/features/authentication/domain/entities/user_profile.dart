import 'package:flutter/foundation.dart';

@immutable
class UserProfile {
  const UserProfile({
    required this.id,
    required this.fullName,
    required this.userName,
    required this.email,
    required this.createdAt,
    required this.updatedAt,
    required this.isActive,
  });

  final String id;
  final String fullName;
  final String userName;
  final String email;
  final DateTime createdAt;
  final DateTime updatedAt;
  final bool isActive;

  String get initials {
    final parts = fullName
        .trim()
        .split(RegExp(r'\s+'))
        .where((p) => p.isNotEmpty)
        .toList(growable: false);
    if (parts.isEmpty) {
      return userName.isNotEmpty ? userName[0].toUpperCase() : '?';
    }
    if (parts.length == 1) {
      return parts.first.substring(0, 1).toUpperCase();
    }
    return (parts.first.substring(0, 1) + parts.last.substring(0, 1))
        .toUpperCase();
  }

  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      other is UserProfile &&
          id == other.id &&
          fullName == other.fullName &&
          userName == other.userName &&
          email == other.email &&
          createdAt == other.createdAt &&
          updatedAt == other.updatedAt &&
          isActive == other.isActive;

  @override
  int get hashCode => Object.hash(
        id,
        fullName,
        userName,
        email,
        createdAt,
        updatedAt,
        isActive,
      );
}
