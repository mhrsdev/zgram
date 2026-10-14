import 'package:flutter/widgets.dart';

/// Direction of a piece of chat text, decided by its first strongly
/// directional character (the same idea as the Unicode bidi "first strong"
/// rule). Persian/Arabic/Hebrew -> RTL; Latin, CJK, Cyrillic, ... -> LTR.
/// Returns null when the text has no letters (digits, emoji, punctuation),
/// so the caller can fall back to the UI direction.
TextDirection? detectTextDirection(String text) {
  for (final rune in text.runes) {
    if (_isRtl(rune)) return TextDirection.rtl;
    if (_isStrongLtr(rune)) return TextDirection.ltr;
  }
  return null;
}

bool _isRtl(int r) =>
    (r >= 0x0590 && r <= 0x08FF) || // Hebrew, Arabic, Syriac, Thaana, Arabic Ext.
    (r >= 0xFB1D && r <= 0xFDFF) || // presentation forms A
    (r >= 0xFE70 && r <= 0xFEFF) || // presentation forms B
    (r >= 0x10800 && r <= 0x10FFF) ||
    (r >= 0x1E800 && r <= 0x1EFFF);

bool _isStrongLtr(int r) =>
    (r >= 0x41 && r <= 0x5A) || // A-Z
    (r >= 0x61 && r <= 0x7A) || // a-z
    (r >= 0xC0 && r <= 0x58F && r != 0xD7 && r != 0xF7) || // Latin ext, Greek, Cyrillic, Armenian
    (r >= 0x0900 && r <= 0x1FFF) || // Indic, Thai, Georgian, Hangul Jamo, ...
    (r >= 0x2C00 && r <= 0x2DFF) ||
    (r >= 0x3040 && r <= 0xA4CF) || // kana, CJK ideographs, Yi
    (r >= 0xAC00 && r <= 0xD7AF) || // Hangul
    (r >= 0x20000 && r <= 0x2FFFF); // CJK ext B+
