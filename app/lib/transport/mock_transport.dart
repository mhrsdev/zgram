import 'dart:async';
import '../models/models.dart';
import 'transport.dart';

/// Offline demo transport so the whole UI works before real transports exist.
/// Login code is always 12345. Phones ending in 0 also ask for the password
/// "zerogram" (to exercise the 2FA screen).
class MockTransport implements Transport {
  MockTransport(this.provider);

  @override
  final Provider provider;

  AuthStep _step = const AuthPhone();
  String _phone = '';
  final _incoming = StreamController<({String chatId, Message message})>.broadcast();
  int _n = 0;

  @override
  AuthStep get step => _step;

  @override
  Future<AuthStep> submitPhone(String phone) async {
    final digits = phone.replaceAll(RegExp(r'[^0-9۰-۹]'), '');
    if (digits.length < 7) throw const AuthError('errPhone');
    _phone = phone.trim();
    return _step = AuthCode(phone: _phone);
  }

  @override
  Future<AuthStep> submitCode(String code) async {
    if (_normalizeDigits(code) != '12345') throw const AuthError('errCode');
    final needsPassword = _normalizeDigits(_phone).endsWith('0');
    return _step = needsPassword
        ? const AuthPassword(hint: 'zerogram')
        : AuthReady(displayName: _nameFor());
  }

  @override
  Future<AuthStep> submitPassword(String password) async {
    if (password != 'zerogram') throw const AuthError('errPassword');
    return _step = AuthReady(displayName: _nameFor());
  }

  String _nameFor() => provider == Provider.telegram ? 'Telegram user' : 'کاربر بله';

  @override
  Future<List<Chat>> loadChats(String accountId) async {
    final now = DateTime.now();
    Message m(String t, bool out, int minsAgo) => Message(
        id: '${_n++}', text: t, outgoing: out, time: now.subtract(Duration(minutes: minsAgo)),
        status: out ? MsgStatus.read : MsgStatus.sent);
    final chats = [
      Chat(id: 'c1', accountId: accountId, provider: provider, title: 'پارسا', unread: 2)
        ..messages.addAll([m('سلام! زیروگرام چطور پیش میره؟', false, 30), m('خوب، لاگین و چت اومد 🎉', true, 25), m('عالیه', false, 3)]),
      Chat(id: 'c2', accountId: accountId, provider: provider, title: 'Alice')
        ..messages.addAll([m('Hey, can you read this: 你好，世界', false, 90), m('Yes — all scripts render fine ✅', true, 80)]),
      Chat(id: 'c3', accountId: accountId, provider: provider, title: 'گروه دوستان', muted: true, unread: 14)
        ..messages.addAll([m('امشب کسی هست؟', false, 200)]),
    ];
    return chats;
  }

  @override
  Future<Message> sendText(Chat chat, String text, {String? replyToText}) async {
    await Future<void>.delayed(const Duration(milliseconds: 150));
    final msg = Message(
        id: '${_n++}', text: text, outgoing: true, time: DateTime.now(), replyToText: replyToText);
    // Demo echo so incoming messages can be seen.
    Timer(const Duration(milliseconds: 900), () {
      _incoming.add((
        chatId: chat.id,
        message: Message(id: '${_n++}', text: 'Echo: $text', outgoing: false, time: DateTime.now()),
      ));
    });
    return msg;
  }

  @override
  Stream<({String chatId, Message message})> get incoming => _incoming.stream;

  @override
  Future<void> logOut() async {
    _step = const AuthPhone();
    await _incoming.close();
  }
}

/// Persian/Arabic digits -> ASCII, so the code "۱۲۳۴۵" works too.
String _normalizeDigits(String s) {
  final b = StringBuffer();
  for (final r in s.runes) {
    if (r >= 0x06F0 && r <= 0x06F9) {
      b.writeCharCode(r - 0x06F0 + 0x30);
    } else if (r >= 0x0660 && r <= 0x0669) {
      b.writeCharCode(r - 0x0660 + 0x30);
    } else {
      b.writeCharCode(r);
    }
  }
  return b.toString().replaceAll(RegExp(r'\s'), '');
}
