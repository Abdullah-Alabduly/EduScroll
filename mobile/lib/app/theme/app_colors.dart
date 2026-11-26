import 'package:flutter/material.dart';

abstract final class AppColors {
  // Brand palette (modern social-first violet + calm teal accents)
  static const Color primary = Color(0xFF7C3AED);
  static const Color primaryDark = Color(0xFF6D28D9);
  static const Color primaryLight = Color(0xFFA78BFA);
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

  // Deep charcoal dark surfaces & crisp ink
  static const Color darkBackground = Color(0xFF0E0F14);
  static const Color darkSurface = Color(0xFF161822);
  static const Color darkSurfaceVariant = Color(0xFF212434);
  static const Color darkBorder = Color(0xFF2E3247);
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
