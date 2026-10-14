import 'dart:async';
import 'package:flutter/material.dart';
import '../models/models.dart';
import '../transport/mock_transport.dart';
import '../transport/transport.dart';

typedef TransportFactory = Transport Function(Provider provider);

/// Holds every signed-in account (any mix of Telegram and Bale), the merged
/// chat list and UI preferences.
class AppState extends ChangeNotifier {
  AppState({TransportFactory? factory}) : _factory = factory ?? MockTransport.new;

  final TransportFactory _factory;

  final List<Account> accounts = [];
  final Map<String, Transport> _transports = {};
  final List<Chat> chats = [];
  final List<StreamSubscription<dynamic>> _subs = [];

  Chat? selected;
  Locale? locale; // null = follow device (fa or en)
  ThemeMode themeMode = ThemeMode.system;
  String? filterAccountId; // null = all accounts

  bool get signedIn => accounts.isNotEmpty;

  Transport newTransport(Provider p) => _factory(p);

  /// Called by the login screen once a transport reaches [AuthReady].
  Future<void> addAccount(Transport t, String phone, String name) async {
    final acc = Account(id: 'acc${accounts.length + 1}-${t.provider.name}', provider: t.provider, phone: phone, name: name);
    accounts.add(acc);
    _transports[acc.id] = t;
    chats.addAll(await t.loadChats(acc.id));
    _subs.add(t.incoming.listen((e) {
      final chat = chats.where((c) => c.accountId == acc.id && c.id == e.chatId).firstOrNull;
      if (chat == null) return;
      chat.messages.add(e.message);
      if (selected != chat) chat.unread++;
      _sort();
      notifyListeners();
    }));
    _sort();
    notifyListeners();
  }

  List<Chat> get visibleChats =>
      filterAccountId == null ? chats : chats.where((c) => c.accountId == filterAccountId).toList();

  void _sort() => chats.sort((a, b) =>
      (b.last?.time ?? DateTime(0)).compareTo(a.last?.time ?? DateTime(0)));

  void select(Chat? c) {
    selected = c;
    c?.unread = 0;
    notifyListeners();
  }

  Future<void> send(Chat chat, String text, {String? replyToText}) async {
    final t = _transports[chat.accountId]!;
    final pending = Message(
        id: 'tmp${DateTime.now().microsecondsSinceEpoch}', text: text, outgoing: true,
        time: DateTime.now(), status: MsgStatus.sending, replyToText: replyToText);
    chat.messages.add(pending);
    _sort();
    notifyListeners();
    final sent = await t.sendText(chat, text, replyToText: replyToText);
    chat.messages[chat.messages.indexOf(pending)] = sent..status = MsgStatus.sent;
    notifyListeners();
  }

  Future<void> logOut(Account a) async {
    await _transports.remove(a.id)?.logOut();
    accounts.remove(a);
    chats.removeWhere((c) => c.accountId == a.id);
    if (selected != null && selected!.accountId == a.id) selected = null;
    if (filterAccountId == a.id) filterAccountId = null;
    notifyListeners();
  }

  void setLocale(Locale? l) { locale = l; notifyListeners(); }
  void setThemeMode(ThemeMode m) { themeMode = m; notifyListeners(); }
  void setFilter(String? id) { filterAccountId = id; notifyListeners(); }

  @override
  void dispose() {
    for (final s in _subs) { s.cancel(); }
    super.dispose();
  }
}
