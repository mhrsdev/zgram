# Zerogram — پیام‌رسان مستقل برای سرور داخل ایران

<div dir="rtl">

زیروگرام یک پیام‌رسان متن‌باز، تک‌سرور و سبک است که برای اجرا روی یک سرور داخل ایران ساخته شده است. بدون اینترنت بین‌الملل کار می‌کند: ثبت‌نام، ورود، ارسال و دریافت پیام و اتصال مجدد فقط به سرور خودتان نیاز دارند.

این پروژه از دو بخش متن‌باز ساخته شده است:

| بخش | مبنا | مجوز |
| --- | --- | --- |
| سرور | [Ferrite](https://github.com/aykutalparslan/Telegram-Server) (کامیت `02b72d79`) | AGPL-3.0-or-later |
| کلاینت Android | [Telegram for Android](https://github.com/DrKLO/Telegram) (نسخهٔ 12.10.1، کامیت `62b56a07`) | GPL-2.0-or-later |

زیروگرام وابسته به شرکت تلگرام نیست و از سوی آن تأیید نشده است. جزئیات منبع و تغییرات در [docs/UPSTREAM.md](docs/UPSTREAM.md) آمده است.

## ایدهٔ اصلی

1. **تاریخچهٔ پیام‌ها روی دستگاه کاربر است**، نه روی سرور. هزینهٔ سرور پایین می‌ماند.
2. **سرور فقط پیام‌های تحویل‌نشده را موقتاً نگه می‌دارد.** وقتی دستگاه گیرنده پیام را روی حافظهٔ پایدار خودش ذخیره کرد و تأیید (ACK) فرستاد، نسخهٔ سرور حذف می‌شود. اگر تأیید نیامد، پس از ۷ روز (قابل‌تنظیم) حذف می‌شود.
3. **همه‌چیز فقط با سرور شما حرف می‌زند.** هیچ وابستگی زمان‌اجرایی به گوگل، Firebase، تلگرام اصلی، CDN یا سرور خارجی وجود ندارد (جزئیات و آزمون در [docs/fa/offline.md](docs/fa/offline.md)).

## ورود و ثبت‌نام

- ثبت‌نام با نام کاربری یکتا، نام نمایشی و ایمیل (ایمیل تأییدنشده است و برای بازیابی استفاده **نمی‌شود**).
- هنگام ثبت‌نام یک secret تصادفی مخصوص حساب ساخته می‌شود؛ برنامه QR و کلید دستی برای Google Authenticator نشان می‌دهد.
- حساب پس از تأیید اولین کد ۶ رقمی (دورهٔ ۳۰ ثانیه) فعال می‌شود.
- ورود بعدی فقط با نام کاربری و کد Authenticator است: بدون رمز ثابت، بدون SMS، بدون حساب گوگل.
- یک دستگاه فعال برای هر حساب؛ ورود جدید نشست قبلی را باطل می‌کند.
- ۱۰ کد بازیابی یک‌بارمصرف (فقط هش آن‌ها ذخیره می‌شود). بازیابی، secret و همهٔ نشست‌های قبلی را باطل می‌کند.

## صاحب سرور، کاربر، چند سرور و گروه‌ها

- صفحهٔ اول برنامه دو انتخاب دارد: **ورود به‌عنوان کاربر** و **ورود به‌عنوان صاحب سرور** (IP + ۱۲ کلمهٔ یکتا مثل کیف پول؛ کلمات هرگز روی شبکه نمی‌روند و روی سرور ذخیره نمی‌شوند). تا وقتی برنامه نصب است احراز هویت دوباره خواسته نمی‌شود؛ روی گوشی دیگر یا بعد از حذف برنامه، همان ۱۲ کلمه لازم است.
- کاربر IP سرور را نمی‌بیند؛ صاحب سرور یک «پیام اتصال» رمزگذاری‌شده می‌گیرد و به کاربران می‌دهد.
- هر گروه یک توکن تصادفی ۳۲ رقمی دارد؛ پیام اتصال + توکن گروه برای پیوستن به آن گروه کافی است.
- هر کاربر یا صاحب سرور می‌تواند هم‌زمان عضو چند سرور باشد و سرور جدید اضافه کند.
- به‌روزرسانی اختیاری برنامه از Releaseهای همین مخزن (GitHub).
- جزئیات: [docs/fa/owner-and-groups.md](docs/fa/owner-and-groups.md)

## دامنهٔ نسخهٔ اول

چت خصوصی متنی، گروه کوچک تا ۵۰ عضو، وضعیت تحویل و حذف محلی پیام‌ها. تماس، کانال، بات، ارسال فایل/رسانه، چت سرّی و ورود با تلفن/SMS در سرور و رابط کلاینت **غیرفعال** هستند.

## رمزنگاری — صادقانه

ارتباط کلاینت و سرور با پروتکل رمزشدهٔ MTProto 2.0 و کلید عمومی سرور شما (pin شده در برنامه) محافظت می‌شود. **رمزنگاری سرتاسری (E2E) وجود ندارد**: سرور پیام را تا لحظهٔ تحویل می‌بیند و روی دیسک خودش (در صف تحویل‌نشده‌ها) نگه می‌دارد. چت سرّی غیرفعال شده تا ادعای نادرست ایجاد نشود.

## شروع سریع

- نصب سرور: [docs/fa/install.md](docs/fa/install.md)
- صاحب سرور، پیام اتصال، توکن گروه و چند سرور: [docs/fa/owner-and-groups.md](docs/fa/owner-and-groups.md)
- انتشار نسخه و به‌روزرسانی خودکار: [docs/fa/releases.md](docs/fa/releases.md)
- به‌روزرسانی و بازگشت: [docs/fa/update-rollback.md](docs/fa/update-rollback.md)
- ساخت و نصب کلاینت Android: [docs/fa/android.md](docs/fa/android.md)
- ذخیرهٔ پیام و محدودیت‌ها: [docs/fa/message-storage.md](docs/fa/message-storage.md)
- امنیت: [docs/fa/security.md](docs/fa/security.md)
- عملیات (پشتیبان‌گیری، پایش، ظرفیت): [docs/fa/operations.md](docs/fa/operations.md)
- کار بدون اینترنت بین‌الملل و push: [docs/fa/offline.md](docs/fa/offline.md)
- نتایج واقعی آزمون‌ها: [docs/fa/test-report.md](docs/fa/test-report.md)

</div>

---

English summary: Zerogram is a single-node messenger for servers inside Iran, built from the open-source Ferrite
(MTProto server, AGPL-3.0) and Telegram for Android (GPL-2.0). Accounts use usernames and authenticator (TOTP)
codes only. Message history lives on users' devices; the server keeps a message only until the recipient's device
acknowledges it (or 7 days). No end-to-end encryption is claimed. Calls, channels, bots, file transfer and secret
chats are disabled. See `docs/fa/` (Persian) for installation, operation and the measured test results.
