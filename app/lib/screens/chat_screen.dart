import 'package:flutter/material.dart';
import '../l10n/strings.dart';
import '../models/models.dart';
import '../state/app_state.dart';
import '../theme/zg_theme.dart';
import '../util/text_dir.dart';

class ChatScreen extends StatefulWidget {
  const ChatScreen({super.key, required this.state, required this.chat, this.showBack = false});
  final AppState state;
  final Chat chat;
  final bool showBack;

  @override
  State<ChatScreen> createState() => _ChatScreenState();
}

class _ChatScreenState extends State<ChatScreen> {
  final _ctrl = TextEditingController();
  Message? _replyTo;

  Future<void> _send() async {
    final text = _ctrl.text.trim();
    if (text.isEmpty) return;
    final reply = _replyTo?.text;
    _ctrl.clear();
    setState(() => _replyTo = null);
    await widget.state.send(widget.chat, text, replyToText: reply);
  }

  @override
  void dispose() { _ctrl.dispose(); super.dispose(); }

  @override
  Widget build(BuildContext context) {
    final s = Strings.of(context);
    final c = ZgColors.of(context);
    return ListenableBuilder(
      listenable: widget.state,
      builder: (context, _) {
        final msgs = widget.chat.messages;
        return Scaffold(
          backgroundColor: c.chatBg,
          appBar: AppBar(
            automaticallyImplyLeading: widget.showBack,
            title: Text(widget.chat.title, maxLines: 1, overflow: TextOverflow.ellipsis),
            actions: [Padding(padding: const EdgeInsetsDirectional.only(end: 12), child: Center(child: Text(s.t(widget.chat.provider.name), style: TextStyle(color: c.subText, fontSize: 12))))],
          ),
          body: Column(children: [
            Expanded(
              child: ListView.builder(
                reverse: true, // anchored to the bottom like Telegram
                padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
                itemCount: msgs.length,
                itemBuilder: (context, i) {
                  final m = msgs[msgs.length - 1 - i];
                  return GestureDetector(
                    onLongPress: () => setState(() => _replyTo = m),
                    child: Bubble(message: m),
                  );
                },
              ),
            ),
            if (_replyTo != null)
              Container(
                color: c.windowBg,
                padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                child: Row(children: [
                  Container(width: 3, height: 28, color: c.accent),
                  const SizedBox(width: 8),
                  Expanded(child: Text(_replyTo!.text, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: c.subText))),
                  IconButton(icon: const Icon(Icons.close, size: 18), onPressed: () => setState(() => _replyTo = null)),
                ]),
              ),
            Container(
              color: c.windowBg,
              padding: const EdgeInsets.fromLTRB(8, 6, 8, 6),
              child: SafeArea(
                top: false,
                child: Row(children: [
                  Expanded(
                    child: ValueListenableBuilder<TextEditingValue>(
                      valueListenable: _ctrl,
                      builder: (context, v, _) => TextField(
                        key: const Key('composer'),
                        controller: _ctrl,
                        minLines: 1, maxLines: 6,
                        textDirection: detectTextDirection(v.text), // follows what is typed
                        decoration: InputDecoration(hintText: s.t('message'), border: InputBorder.none, contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10)),
                        onSubmitted: (_) => _send(),
                      ),
                    ),
                  ),
                  IconButton(key: const Key('send'), icon: Icon(Icons.send, color: c.accent), onPressed: _send),
                ]),
              ),
            ),
          ]),
        );
      },
    );
  }
}

class Bubble extends StatelessWidget {
  const Bubble({super.key, required this.message});
  final Message message;

  @override
  Widget build(BuildContext context) {
    final c = ZgColors.of(context);
    final m = message;
    final out = m.outgoing;
    final dir = detectTextDirection(m.text) ?? Directionality.of(context);
    final ui = Directionality.of(context);
    // Outgoing bubbles sit on the "end" side of the UI direction.
    final alignEnd = out ? ui == TextDirection.ltr : ui == TextDirection.rtl;
    const big = Radius.circular(16), small = Radius.circular(6);
    // The small "tail" corner is on the side the bubble is aligned to, bottom.
    final tailRight = alignEnd;
    final radius = BorderRadius.only(
      topLeft: big, topRight: big,
      bottomLeft: tailRight ? big : small,
      bottomRight: tailRight ? small : big,
    );
    final time = '${m.time.hour.toString().padLeft(2, '0')}:${m.time.minute.toString().padLeft(2, '0')}';
    return Align(
      alignment: alignEnd ? Alignment.centerRight : Alignment.centerLeft,
      child: Container(
        margin: const EdgeInsets.symmetric(vertical: 2),
        constraints: const BoxConstraints(maxWidth: 430),
        padding: const EdgeInsets.fromLTRB(10, 6, 10, 4),
        decoration: BoxDecoration(color: out ? c.msgOutBg : c.msgInBg, borderRadius: radius),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisSize: MainAxisSize.min, children: [
          if (m.replyToText != null)
            Container(
              margin: const EdgeInsets.only(bottom: 4),
              padding: const EdgeInsetsDirectional.only(start: 8),
              decoration: BoxDecoration(border: BorderDirectional(start: BorderSide(color: c.accent, width: 2))),
              child: Text(m.replyToText!, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: c.accentText, fontSize: 13)),
            ),
          Directionality(
            textDirection: dir,
            child: SizedBox(
              width: null,
              child: Text(m.text, textAlign: TextAlign.start, style: TextStyle(color: out ? c.msgOutText : c.msgInText, fontSize: 15.5)),
            ),
          ),
          const SizedBox(height: 2),
          Align(
            alignment: AlignmentDirectional.centerEnd,
            child: Row(mainAxisSize: MainAxisSize.min, children: [
              if (m.edited) Text('${Strings.of(context).t('edited')}  ', style: TextStyle(fontSize: 11, color: out ? c.msgOutDate : c.msgInDate)),
              Text(time, style: TextStyle(fontSize: 11, color: out ? c.msgOutDate : c.msgInDate)),
              if (out) ...[
                const SizedBox(width: 3),
                Icon(
                  m.status == MsgStatus.sending ? Icons.schedule : (m.status == MsgStatus.read ? Icons.done_all : Icons.done),
                  size: 14, color: c.tick,
                ),
              ],
            ]),
          ),
        ]),
      ),
    );
  }
}
