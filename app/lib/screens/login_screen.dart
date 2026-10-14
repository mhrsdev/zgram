import 'package:flutter/material.dart';
import '../l10n/strings.dart';
import '../models/models.dart';
import '../state/app_state.dart';
import '../theme/zg_theme.dart';
import '../transport/transport.dart';

/// Sign-in for one account: pick messenger -> phone -> code -> (2FA password).
/// Used both for the first launch and for "Add account".
class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key, required this.state, this.canCancel = false});
  final AppState state;
  final bool canCancel;

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  Provider _provider = Provider.telegram;
  late Transport _t = widget.state.newTransport(_provider);
  AuthStep _step = const AuthPhone();
  final _input = TextEditingController();
  String _phone = '';
  String? _error;
  bool _busy = false;

  void _pick(Provider p) {
    if (p == _provider) return;
    setState(() {
      _provider = p;
      _t = widget.state.newTransport(p);
      _step = const AuthPhone();
      _error = null;
      _input.clear();
    });
  }

  Future<void> _submit() async {
    setState(() { _busy = true; _error = null; });
    try {
      final next = switch (_step) {
        AuthPhone() => await _t.submitPhone(_input.text),
        AuthCode() => await _t.submitCode(_input.text),
        AuthPassword() => await _t.submitPassword(_input.text),
        AuthReady() => _step,
      };
      if (_step is AuthPhone) _phone = _input.text.trim();
      if (next is AuthReady) {
        await widget.state.addAccount(_t, _phone, next.displayName);
        if (mounted && widget.canCancel) Navigator.of(context).pop();
        return;
      }
      setState(() { _step = next; _input.clear(); });
    } on AuthError catch (e) {
      setState(() => _error = e.key);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  void dispose() { _input.dispose(); super.dispose(); }

  @override
  Widget build(BuildContext context) {
    final s = Strings.of(context);
    final c = ZgColors.of(context);
    final (label, hint, obscure, kbd) = switch (_step) {
      AuthPhone() => (s.t('phone'), s.t('phoneHint'), false, TextInputType.phone),
      AuthCode() => (s.t('code'), '•••••', false, TextInputType.number),
      AuthPassword(hint: final h) => (s.t('password'), h ?? s.t('passwordHint'), true, TextInputType.visiblePassword),
      AuthReady() => ('', '', false, TextInputType.text),
    };
    return Scaffold(
      appBar: widget.canCancel ? AppBar(title: Text(s.t('addAccount'))) : null,
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 380),
            child: ListView(
              padding: const EdgeInsets.all(24),
              shrinkWrap: true,
              children: [
                CircleAvatar(radius: 44, backgroundColor: c.accent, child: const Text('Z', style: TextStyle(fontSize: 40, color: Colors.white, fontWeight: FontWeight.w700))),
                const SizedBox(height: 16),
                Text(s.t('app'), textAlign: TextAlign.center, style: Theme.of(context).textTheme.headlineMedium),
                const SizedBox(height: 24),
                if (_step is AuthPhone) ...[
                  Text(s.t('chooseMessenger'), style: TextStyle(color: c.subText)),
                  const SizedBox(height: 8),
                  SegmentedButton<Provider>(
                    segments: [
                      ButtonSegment(value: Provider.telegram, label: Text(s.t('telegram'))),
                      ButtonSegment(value: Provider.bale, label: Text(s.t('bale'))),
                    ],
                    selected: {_provider},
                    onSelectionChanged: (v) => _pick(v.first),
                  ),
                  const SizedBox(height: 16),
                ],
                if (_step is AuthCode) Padding(
                  padding: const EdgeInsets.only(bottom: 12),
                  child: Text('${s.t('codeSent')} $_phone', style: TextStyle(color: c.subText)),
                ),
                TextField(
                  key: const Key('auth_input'),
                  controller: _input,
                  autofocus: true,
                  obscureText: obscure,
                  keyboardType: kbd,
                  textDirection: TextDirection.ltr, // phone, code and password are typed LTR
                  decoration: InputDecoration(labelText: label, hintText: hint, errorText: _error == null ? null : s.t(_error!), border: const OutlineInputBorder()),
                  onSubmitted: (_) => _submit(),
                ),
                const SizedBox(height: 16),
                FilledButton(
                  key: const Key('auth_next'),
                  onPressed: _busy ? null : _submit,
                  child: _busy ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2)) : Text(s.t('next')),
                ),
                const SizedBox(height: 12),
                Text(s.t('mockNotice'), textAlign: TextAlign.center, style: TextStyle(color: c.subText, fontSize: 12)),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
