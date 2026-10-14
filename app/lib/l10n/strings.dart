import 'package:flutter/widgets.dart';

/// App UI language: Persian and English only. (Chat content is never
/// translated or restricted; any script renders.)
class Strings {
  Strings(this.locale);
  final Locale locale;

  static const supported = [Locale('fa'), Locale('en')];

  static Strings of(BuildContext context) =>
      Localizations.of<Strings>(context, Strings)!;

  static const delegate = _StringsDelegate();

  bool get isFa => locale.languageCode == 'fa';
  String t(String key) => (isFa ? _fa : _en)[key] ?? _en[key] ?? key;

  static const _en = {
    'app': 'Zerogram',
    'appShort': 'Zgram',
    'chooseMessenger': 'Choose a messenger',
    'telegram': 'Telegram',
    'bale': 'Bale',
    'phone': 'Phone number',
    'phoneHint': '+98 912 345 6789',
    'next': 'Next',
    'code': 'Login code',
    'codeSent': 'We sent a code to',
    'password': 'Two-step password',
    'passwordHint': 'Enter your password',
    'back': 'Back',
    'errPhone': 'Enter a valid phone number',
    'errCode': 'Wrong code, try again',
    'errPassword': 'Wrong password',
    'accounts': 'Accounts',
    'addAccount': 'Add account',
    'logout': 'Log out',
    'search': 'Search',
    'message': 'Message',
    'noChats': 'No chats yet',
    'selectChat': 'Select a chat to start messaging',
    'language': 'Language',
    'dark': 'Dark mode',
    'online': 'online',
    'edited': 'edited',
    'today': 'Today',
    'mockNotice': 'Demo mode: code is 12345',
  };

  static const _fa = {
    'app': 'زیروگرام',
    'appShort': 'زی‌گرام',
    'chooseMessenger': 'پیام‌رسان را انتخاب کنید',
    'telegram': 'تلگرام',
    'bale': 'بله',
    'phone': 'شماره تلفن',
    'phoneHint': '۰۹۱۲ ۳۴۵ ۶۷۸۹',
    'next': 'ادامه',
    'code': 'کد ورود',
    'codeSent': 'کد به این شماره ارسال شد:',
    'password': 'رمز دومرحله‌ای',
    'passwordHint': 'رمز را وارد کنید',
    'back': 'بازگشت',
    'errPhone': 'شماره تلفن معتبر وارد کنید',
    'errCode': 'کد اشتباه است',
    'errPassword': 'رمز اشتباه است',
    'accounts': 'حساب‌ها',
    'addAccount': 'افزودن حساب',
    'logout': 'خروج',
    'search': 'جستجو',
    'message': 'پیام',
    'noChats': 'هنوز گفتگویی نیست',
    'selectChat': 'برای شروع، یک گفتگو را انتخاب کنید',
    'language': 'زبان',
    'dark': 'حالت شب',
    'online': 'آنلاین',
    'edited': 'ویرایش‌شده',
    'today': 'امروز',
    'mockNotice': 'حالت نمایشی: کد ۱۲۳۴۵ است',
  };
}

class _StringsDelegate extends LocalizationsDelegate<Strings> {
  const _StringsDelegate();
  @override
  bool isSupported(Locale l) => l.languageCode == 'fa' || l.languageCode == 'en';
  @override
  Future<Strings> load(Locale l) async => Strings(l);
  @override
  bool shouldReload(_StringsDelegate old) => false;
}
