import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:zerogram/util/text_dir.dart';

void main() {
  test('Persian is RTL', () => expect(detectTextDirection('سلام دنیا'), TextDirection.rtl));
  test('English is LTR', () => expect(detectTextDirection('hello'), TextDirection.ltr));
  test('Chinese is LTR', () => expect(detectTextDirection('你好，世界'), TextDirection.ltr));
  test('Japanese and Korean are LTR', () {
    expect(detectTextDirection('こんにちは'), TextDirection.ltr);
    expect(detectTextDirection('안녕하세요'), TextDirection.ltr);
  });
  test('Arabic and Hebrew are RTL', () {
    expect(detectTextDirection('مرحبا'), TextDirection.rtl);
    expect(detectTextDirection('שלום'), TextDirection.rtl);
  });
  test('first strong character wins', () {
    expect(detectTextDirection('123 سلام hello'), TextDirection.rtl);
    expect(detectTextDirection('😀 hello سلام'), TextDirection.ltr);
  });
  test('no letters -> null', () {
    expect(detectTextDirection('12345'), isNull);
    expect(detectTextDirection('😀🎉'), isNull);
    expect(detectTextDirection(''), isNull);
  });
}
