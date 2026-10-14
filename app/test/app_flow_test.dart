import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:zerogram/main.dart';
import 'package:zerogram/state/app_state.dart';

Future<void> _login(WidgetTester t, String phone, {String? password}) async {
  await t.enterText(find.byKey(const Key('auth_input')), phone);
  await t.tap(find.byKey(const Key('auth_next')));
  await t.pumpAndSettle();
  await t.enterText(find.byKey(const Key('auth_input')), '12345');
  await t.tap(find.byKey(const Key('auth_next')));
  await t.pumpAndSettle();
  if (password != null) {
    await t.enterText(find.byKey(const Key('auth_input')), password);
    await t.tap(find.byKey(const Key('auth_next')));
    await t.pumpAndSettle();
  }
}

void main() {
  testWidgets('login with phone + code, see chats, send a message', (t) async {
    t.view.physicalSize = const Size(1200, 800);
    t.view.devicePixelRatio = 1;
    addTearDown(t.view.reset);
    final state = AppState();
    await t.pumpWidget(ZerogramApp(state: state));
    await t.pumpAndSettle(); // localizations load asynchronously
    await _login(t, '09123456781');
    expect(state.accounts.length, 1);
    expect(find.text('پارسا'), findsWidgets);
    await t.tap(find.text('Alice').first);
    await t.pumpAndSettle();
    await t.enterText(find.byKey(const Key('composer')), 'سلام 你好 hi');
    await t.tap(find.byKey(const Key('send')));
    await t.pump(const Duration(seconds: 2));
    await t.pumpAndSettle();
    expect(find.text('سلام 你好 hi'), findsOneWidget);
    expect(find.text('Echo: سلام 你好 hi'), findsNWidgets(2)); // list preview + bubble
  });

  testWidgets('2FA password step and wrong code error', (t) async {
    final state = AppState();
    await t.pumpWidget(ZerogramApp(state: state));
    await t.pumpAndSettle(); // localizations load asynchronously
    await t.enterText(find.byKey(const Key('auth_input')), '09123456780');
    await t.tap(find.byKey(const Key('auth_next')));
    await t.pumpAndSettle();
    await t.enterText(find.byKey(const Key('auth_input')), '00000');
    await t.tap(find.byKey(const Key('auth_next')));
    await t.pumpAndSettle();
    expect(find.text('Wrong code, try again'), findsOneWidget);
    await t.enterText(find.byKey(const Key('auth_input')), '12345');
    await t.tap(find.byKey(const Key('auth_next')));
    await t.pumpAndSettle();
    expect(find.text('Two-step password'), findsOneWidget);
    await t.enterText(find.byKey(const Key('auth_input')), 'zerogram');
    await t.tap(find.byKey(const Key('auth_next')));
    await t.pumpAndSettle();
    expect(state.signedIn, isTrue);
  });
}
