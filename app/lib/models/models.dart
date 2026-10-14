enum Provider { telegram, bale }

enum MsgStatus { sending, sent, read }

class Account {
  Account({required this.id, required this.provider, required this.phone, required this.name});
  final String id;
  final Provider provider;
  final String phone;
  final String name;
}

class Message {
  Message({
    required this.id,
    required this.text,
    required this.outgoing,
    required this.time,
    this.status = MsgStatus.sent,
    this.replyToText,
    this.edited = false,
  });
  final String id;
  String text;
  final bool outgoing;
  final DateTime time;
  MsgStatus status;
  final String? replyToText;
  bool edited;
}

class Chat {
  Chat({required this.id, required this.accountId, required this.provider, required this.title, this.unread = 0, this.muted = false});
  final String id;
  final String accountId;
  final Provider provider;
  final String title;
  int unread;
  bool muted;
  final List<Message> messages = [];

  Message? get last => messages.isEmpty ? null : messages.last;
}
