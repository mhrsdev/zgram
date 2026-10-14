import 'package:flutter/material.dart';
import '../l10n/strings.dart';
import '../models/models.dart';
import '../state/app_state.dart';
import '../theme/zg_theme.dart';
import '../util/text_dir.dart';
import 'chat_screen.dart';
import 'login_screen.dart';

/// Two-pane on wide windows (>= 640 px, like Telegram Desktop), single pane on phones.
class HomeScreen extends StatelessWidget {
  const HomeScreen({super.key, required this.state});
  final AppState state;

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: state,
      builder: (context, _) {
        final wide = MediaQuery.sizeOf(context).width >= 640;
        final list = ChatList(state: state, onOpen: (chat) {
          state.select(chat);
          if (!wide) {
            Navigator.of(context).push(MaterialPageRoute<void>(
              builder: (_) => ChatScreen(state: state, chat: chat, showBack: true),
            )).then((_) => state.select(null));
          }
        });
        final drawer = AccountsDrawer(state: state);
        if (!wide) {
          return Scaffold(drawer: drawer, appBar: _bar(context), body: list);
        }
        final s = Strings.of(context);
        return Scaffold(
          drawer: drawer,
          appBar: _bar(context),
          body: Row(children: [
            SizedBox(width: 340, child: list),
            VerticalDivider(width: 1, color: ZgColors.of(context).divider),
            Expanded(
              child: state.selected == null
                  ? Container(color: ZgColors.of(context).chatBg, alignment: Alignment.center, child: Text(s.t('selectChat')))
                  : ChatScreen(key: ValueKey(state.selected!.id + state.selected!.accountId), state: state, chat: state.selected!),
            ),
          ]),
        );
      },
    );
  }

  AppBar _bar(BuildContext context) {
    final s = Strings.of(context);
    final narrow = MediaQuery.sizeOf(context).width < 360;
    return AppBar(title: Text(narrow ? s.t('appShort') : s.t('app')));
  }
}

class ChatList extends StatelessWidget {
  const ChatList({super.key, required this.state, required this.onOpen});
  final AppState state;
  final void Function(Chat) onOpen;

  @override
  Widget build(BuildContext context) {
    final s = Strings.of(context);
    final c = ZgColors.of(context);
    final chats = state.visibleChats;
    if (chats.isEmpty) return Center(child: Text(s.t('noChats')));
    return ListView.builder(
      itemCount: chats.length,
      itemBuilder: (context, i) {
        final chat = chats[i];
        final last = chat.last;
        final active = state.selected == chat;
        return Material(
          color: active ? c.accent.withValues(alpha: .18) : Colors.transparent,
          child: InkWell(
            onTap: () => onOpen(chat),
            child: SizedBox(
              height: 62,
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 12),
                child: Row(children: [
                  _Avatar(title: chat.title, provider: chat.provider),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Column(mainAxisAlignment: MainAxisAlignment.center, crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Row(children: [
                        Expanded(child: _Dir(chat.title, maxLines: 1, style: const TextStyle(fontWeight: FontWeight.w600))),
                        if (last != null) Text(_hm(last.time), style: TextStyle(fontSize: 12, color: c.subText)),
                      ]),
                      const SizedBox(height: 2),
                      Row(children: [
                        Expanded(child: _Dir(last?.text ?? '', maxLines: 1, style: TextStyle(color: c.subText))),
                        if (chat.unread > 0)
                          Container(
                            padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 1),
                            decoration: BoxDecoration(color: chat.muted ? c.subText : c.unreadBg, borderRadius: BorderRadius.circular(12)),
                            child: Text('${chat.unread}', style: const TextStyle(color: Colors.white, fontSize: 12)),
                          ),
                      ]),
                    ]),
                  ),
                ]),
              ),
            ),
          ),
        );
      },
    );
  }
}

/// Text whose direction follows its own first strong character.
class _Dir extends StatelessWidget {
  const _Dir(this.text, {this.style, this.maxLines});
  final String text;
  final TextStyle? style;
  final int? maxLines;

  @override
  Widget build(BuildContext context) {
    final d = detectTextDirection(text) ?? Directionality.of(context);
    return Directionality(
      textDirection: d,
      child: Text(text, style: style, maxLines: maxLines, overflow: TextOverflow.ellipsis, textAlign: TextAlign.start),
    );
  }
}

class _Avatar extends StatelessWidget {
  const _Avatar({required this.title, required this.provider});
  final String title;
  final Provider provider;

  @override
  Widget build(BuildContext context) {
    final c = ZgColors.of(context);
    final initial = title.isEmpty ? '?' : String.fromCharCode(title.runes.first);
    return Stack(clipBehavior: Clip.none, children: [
      CircleAvatar(radius: 23, backgroundColor: c.accent, child: Text(initial, style: const TextStyle(color: Colors.white, fontSize: 18))),
      PositionedDirectional(
        end: -2, bottom: -2,
        child: Container(
          width: 16, height: 16,
          decoration: BoxDecoration(shape: BoxShape.circle, color: provider == Provider.telegram ? const Color(0xFF2AABEE) : const Color(0xFF3DB04E), border: Border.all(color: c.windowBg, width: 2)),
        ),
      ),
    ]);
  }
}

String _hm(DateTime t) => '${t.hour.toString().padLeft(2, '0')}:${t.minute.toString().padLeft(2, '0')}';

class AccountsDrawer extends StatelessWidget {
  const AccountsDrawer({super.key, required this.state});
  final AppState state;

  @override
  Widget build(BuildContext context) {
    final s = Strings.of(context);
    final c = ZgColors.of(context);
    return Drawer(
      width: 274,
      child: SafeArea(
        child: ListView(children: [
          Padding(padding: const EdgeInsets.all(16), child: Text(s.t('accounts'), style: Theme.of(context).textTheme.titleMedium)),
          RadioGroup<String?>(
            groupValue: state.filterAccountId,
            onChanged: (v) { state.setFilter(v); Navigator.of(context).pop(); },
            child: Column(children: [
              for (final a in state.accounts)
                ListTile(
                  leading: _Avatar(title: a.name, provider: a.provider),
                  title: Text(a.name),
                  subtitle: Text('${s.t(a.provider.name)} • ${a.phone}'),
                  trailing: IconButton(icon: Icon(Icons.logout, color: c.danger), tooltip: s.t('logout'), onPressed: () => state.logOut(a)),
                  onTap: () { state.setFilter(a.id); Navigator.of(context).pop(); },
                ),
            ]),
          ),
          ListTile(
            leading: const Icon(Icons.add),
            title: Text(s.t('addAccount')),
            onTap: () {
              Navigator.of(context).pop();
              Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => LoginScreen(state: state, canCancel: true)));
            },
          ),
          const Divider(),
          ListTile(
            leading: const Icon(Icons.language),
            title: Text(s.t('language')),
            trailing: SegmentedButton<String>(
              showSelectedIcon: false,
              segments: const [ButtonSegment(value: 'fa', label: Text('فا')), ButtonSegment(value: 'en', label: Text('EN'))],
              selected: {Localizations.localeOf(context).languageCode == 'fa' ? 'fa' : 'en'},
              onSelectionChanged: (v) => state.setLocale(Locale(v.first)),
            ),
          ),
          SwitchListTile(
            secondary: const Icon(Icons.dark_mode_outlined),
            title: Text(s.t('dark')),
            value: Theme.of(context).brightness == Brightness.dark,
            onChanged: (v) => state.setThemeMode(v ? ThemeMode.dark : ThemeMode.light),
          ),
        ]),
      ),
    );
  }
}
