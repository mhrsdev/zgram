import 'package:flutter/material.dart';

/// Colors taken from the reference palette in docs/reference/ui-design.md
/// (day and night). Exposed as a ThemeExtension so widgets read `ZgColors.of(context)`.
@immutable
class ZgColors extends ThemeExtension<ZgColors> {
  const ZgColors({
    required this.windowBg, required this.windowBgOver, required this.text,
    required this.subText, required this.accent, required this.accentText,
    required this.sideBg, required this.sideActiveBg, required this.msgInBg,
    required this.msgOutBg, required this.msgInText, required this.msgOutText,
    required this.msgInDate, required this.msgOutDate, required this.tick,
    required this.unreadBg, required this.chatBg, required this.divider,
    required this.danger,
  });

  final Color windowBg, windowBgOver, text, subText, accent, accentText, sideBg,
      sideActiveBg, msgInBg, msgOutBg, msgInText, msgOutText, msgInDate,
      msgOutDate, tick, unreadBg, chatBg, divider, danger;

  static const day = ZgColors(
    windowBg: Color(0xFFFFFFFF), windowBgOver: Color(0xFFF1F1F1),
    text: Color(0xFF000000), subText: Color(0xFF999999),
    accent: Color(0xFF40A7E3), accentText: Color(0xFF168ACD),
    sideBg: Color(0xFF293A4C), sideActiveBg: Color(0xFF17212B),
    msgInBg: Color(0xFFFFFFFF), msgOutBg: Color(0xFFEFFDDE),
    msgInText: Color(0xFF000000), msgOutText: Color(0xFF000000),
    msgInDate: Color(0xFFA0ACB6), msgOutDate: Color(0xFF6DB566),
    tick: Color(0xFF57B84C), unreadBg: Color(0xFF40A7E3),
    chatBg: Color(0xFFDDE7EE), divider: Color(0xFFE0E0E0),
    danger: Color(0xFFD14E4E),
  );

  static const night = ZgColors(
    windowBg: Color(0xFF17212B), windowBgOver: Color(0xFF232E3C),
    text: Color(0xFFF5F5F5), subText: Color(0xFF708499),
    accent: Color(0xFF5288C1), accentText: Color(0xFF6AB3F3),
    sideBg: Color(0xFF0E1621), sideActiveBg: Color(0xFF25303E),
    msgInBg: Color(0xFF182533), msgOutBg: Color(0xFF2B5278),
    msgInText: Color(0xFFF5F5F5), msgOutText: Color(0xFFE4ECF2),
    msgInDate: Color(0xFF6D7F8F), msgOutDate: Color(0xFF7DA8D3),
    tick: Color(0xFF6BBFFF), unreadBg: Color(0xFF4082BC),
    chatBg: Color(0xFF0E1621), divider: Color(0xFF202B36),
    danger: Color(0xFFEC3942),
  );

  static ZgColors of(BuildContext c) => Theme.of(c).extension<ZgColors>()!;

  @override
  ZgColors copyWith() => this;
  @override
  ZgColors lerp(ThemeExtension<ZgColors>? other, double t) => this;
}

/// System-font fallbacks so any script a contact writes in (Persian, Latin,
/// CJK, emoji, ...) renders instead of showing tofu boxes. Bundle Vazirmatn /
/// Noto fonts as assets later for pixel-identical text across platforms.
const zgFontFallback = <String>[
  'Vazirmatn', 'Noto Sans', 'Noto Sans Arabic', 'Noto Sans CJK SC',
  'Noto Sans JP', 'Noto Sans KR', 'Noto Color Emoji', 'Apple Color Emoji',
  'Segoe UI Emoji', 'sans-serif',
];

ThemeData zgTheme(Brightness b) {
  final c = b == Brightness.dark ? ZgColors.night : ZgColors.day;
  return ThemeData(
    useMaterial3: true,
    brightness: b,
    colorScheme: ColorScheme.fromSeed(seedColor: c.accent, brightness: b)
        .copyWith(primary: c.accent, surface: c.windowBg),
    scaffoldBackgroundColor: c.windowBg,
    fontFamilyFallback: zgFontFallback,
    dividerColor: c.divider,
    extensions: [c],
  );
}
