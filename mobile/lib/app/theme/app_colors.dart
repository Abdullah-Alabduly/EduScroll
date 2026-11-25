import 'package:flutter/material.dart';

abstract final class AppColors {
  // Brand palette (modern educational indigo + calm teal)
  static const Color primary = Color(0xFF2563EB);
  static const Color primaryDark = Color(0xFF1D4ED8);
  static const Color primaryLight = Color(0xFF60A5FA);
  static const Color secondary = Color(0xFF0D9488);
  static const Color secondaryLight = Color(0xFF2DD4BF);

  // Light surfaces & ink
  static const Color lightBackground = Color(0xFFF8FAFC);
  static const Color lightSurface = Color(0xFFFFFFFF);
  static const Color lightSurfaceVariant = Color(0xFFF1F5F9);
  static const Color lightBorder = Color(0xFFE2E8F0);
  static const Color lightTextPrimary = Color(0xFF0F172A);
  static const Color lightTextSecondary = Color(0xFF475569);
  static const Color lightTextMuted = Color(0xFF64748B);

  // Dark surfaces & ink
  static const Color darkBackground = Color(0xFF0B1120);
  static const Color darkSurface = Color(0xFF111827);
  static const Color darkSurfaceVariant = Color(0xFF1E293B);
  static const Color darkBorder = Color(0xFF334155);
  static const Color darkTextPrimary = Color(0xFFF8FAFC);
  static const Color darkTextSecondary = Color(0xFFCBD5E1);
  static const Color darkTextMuted = Color(0xFF94A3B8);

  // Semantic colors
  static const Color error = Color(0xFFDC2626);
  static const Color errorContainerLight = Color(0xFFFEF2F2);
  static const Color errorContainerDark = Color(0xFF450A0A);
  static const Color success = Color(0xFF15803D);
  static const Color successContainerLight = Color(0xFFF0FDF4);
  static const Color successContainerDark = Color(0xFF052E16);
  static const Color warning = Color(0xFFB45309);
}
