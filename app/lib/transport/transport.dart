import '../models/models.dart';

/// Login steps a transport can ask for. Each provider maps its own protocol
/// (TDLib authorizationState* for Telegram, Bale's client protocol later)
/// onto this neutral state machine. See docs/reference/login-flow.md.
sealed class AuthStep {
  const AuthStep();
}

class AuthPhone extends AuthStep {
  const AuthPhone();
}

class AuthCode extends AuthStep {
  const AuthCode({required this.phone, this.length = 5});
  final String phone;
  final int length;
}

class AuthPassword extends AuthStep {
  const AuthPassword({this.hint});
  final String? hint;
}

class AuthReady extends AuthStep {
  const AuthReady({required this.displayName});
  final String displayName;
}

/// Thrown by submit* calls. [key] is a normalised error key (see Strings).
class AuthError implements Exception {
  const AuthError(this.key);
  final String key;
  @override
  String toString() => 'AuthError($key)';
}

/// One signed-in or signing-in account on one messenger.
/// Real implementations: TdlibTransport (Telegram), BaleTransport.
abstract class Transport {
  Provider get provider;

  /// Current step; starts at [AuthPhone].
  AuthStep get step;

  Future<AuthStep> submitPhone(String phone);
  Future<AuthStep> submitCode(String code);
  Future<AuthStep> submitPassword(String password);

  Future<List<Chat>> loadChats(String accountId);

  /// Sends text and returns the stored message (status `sent`).
  Future<Message> sendText(Chat chat, String text, {String? replyToText});

  /// Incoming messages for any chat of this account.
  Stream<({String chatId, Message message})> get incoming;

  Future<void> logOut();
}
