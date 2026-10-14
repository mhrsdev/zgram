import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'l10n/strings.dart';
import 'screens/home_screen.dart';
import 'screens/login_screen.dart';
import 'state/app_state.dart';
import 'theme/zg_theme.dart';

void main() => runApp(ZerogramApp(state: AppState()));

class ZerogramApp extends StatelessWidget {
  const ZerogramApp({super.key, required this.state});
  final AppState state;

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: state,
      builder: (context, _) => MaterialApp(
        title: 'Zerogram',
        debugShowCheckedModeBanner: false,
        theme: zgTheme(Brightness.light),
        darkTheme: zgTheme(Brightness.dark),
        themeMode: state.themeMode,
        locale: state.locale,
        supportedLocales: Strings.supported,
        localizationsDelegates: const [
          Strings.delegate,
          GlobalMaterialLocalizations.delegate,
          GlobalWidgetsLocalizations.delegate,
          GlobalCupertinoLocalizations.delegate,
        ],
        // Unsupported device languages fall back to English.
        localeResolutionCallback: (l, supported) =>
            supported.firstWhere((s) => s.languageCode == l?.languageCode, orElse: () => const Locale('en')),
        home: state.signedIn
            ? HomeScreen(state: state)
            : LoginScreen(state: state),
      ),
    );
  }
}
