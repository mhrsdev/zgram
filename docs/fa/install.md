# نصب سرور

<div dir="rtl">

هدف: یک سرور Linux داخل ایران با ۲ vCPU، ۲ تا ۴ گیگابایت RAM و SSD چهل گیگابایتی. ظرفیت دقیق تضمین نمی‌شود؛ اعداد اندازه‌گیری‌شده در [test-report.md](test-report.md) آمده است.

## پیش‌نیازها

- Linux (Ubuntu 22.04/24.04 یا Debian 12 آزموده شده‌اند به‌عنوان مبنا) با Docker Engine 24+ و Compose v2.
- یک IP یا نام دامنه که کاربران داخل ایران به آن دسترسی دارند، و پورت TCP `5222` (قابل‌تغییر) باز.
- سرور هیچ اتصال خروجی به اینترنت لازم ندارد (جز برای ساخت image، اگر آن را روی خود سرور می‌سازید).

## روش ۱ — سرور بدون اینترنت (پیشنهادی برای ایران)

روی یک ماشین **با اینترنت** (مثلاً لپ‌تاپ):

```sh
git clone https://github.com/mhrsdev/zgram.git zerogram && cd zerogram
./deploy/zerogram/scripts/offline-bundle.sh dist
```

سه فایل ساخته می‌شود: `zerogram-image.tar.gz`، `zerogram-deploy.tar.gz` و `SHA256SUMS`. آن‌ها را (با USB یا scp) به سرور ببرید و درستی‌شان را بررسی کنید:

```sh
sha256sum -c SHA256SUMS
tar -xzf zerogram-deploy.tar.gz && cd zerogram
gzip -dc ../zerogram-image.tar.gz | docker load
```

## روش ۲ — ساخت روی خود سرور (نیاز به دسترسی NuGet)

```sh
git clone https://github.com/mhrsdev/zgram.git zerogram && cd zerogram/deploy/zerogram
```

## راه‌اندازی (هر دو روش)

```sh
./scripts/install.sh                 # .env می‌سازد و master key تصادفی می‌گذارد
nano .env                            # ZEROGRAM_PUBLIC_ADDRESS را به IP/دامنهٔ سرور تغییر دهید
docker compose up -d                 # (در روش ۲: docker compose up -d --build)
docker compose ps                    # وضعیت healthy باید ظاهر شود
```

`install.sh` یک **رمز صاحب سرور** تصادفی ۲۴ نویسه‌ای می‌سازد و یک‌بار چاپ می‌کند (در `.env` هم به‌صورت `ZEROGRAM_OWNER_PASSWORD` می‌ماند). مرحلهٔ بعد در برنامه است:

1. برنامه را باز کنید ← «ورود به‌عنوان صاحب سرور» ← IP سرور و رمز صاحب سرور.
2. برنامه خودکار وصل می‌شود و صفحهٔ «مدیریت سرور» را نشان می‌دهد: **پیام اتصال رمزگذاری‌شده** (آن را به کاربران بدهید) و **توکن ۳۲ رقمی هر گروه**. شرح کامل: [owner-and-groups.md](owner-and-groups.md).

### نکته‌های مهم

1. **master key را بیرون از سرور هم نگه دارید.** `ZEROGRAM_MASTER_KEY` در `.env` کلید رمزگذاری secretهای TOTP است. با از دست دادنش، کاربران باید با کدهای بازیابی دوباره راه‌اندازی کنند. فایل `.env` را commit نکنید.
2. کلید RSA سرور در اولین اجرا ساخته می‌شود (`default-private.key` در volume، فقط خوانا برای مالک). کلید نمونه یا اعتبارنامهٔ نمونه‌ای در این پروژه وجود ندارد.
3. کاربران IP سرور را نمی‌بینند و تایپ نمی‌کنند: صاحب سرور از برنامه «پیام اتصال» رمزگذاری‌شده را می‌گیرد و به آن‌ها می‌دهد (توضیح صادقانهٔ محدودیتش در [security.md](security.md)). رمز صاحب سرور را فقط خودتان نگه دارید و به کسی ندهید.
4. فایروال: دو پورت `5222/tcp` (پروتکل پیام‌رسان) و `5223/tcp` (فقط برای اینکه برنامهٔ صاحب سرور کلید **عمومی** سرور را از روی IP بگیرد) باید باز باشند. اگر پورت اول را عوض می‌کنید، پورت دوم باید «پورت اول + ۱» بماند (`ZEROGRAM_BOOTSTRAP_PORT_HOST`). پورت سلامت (`8081`) فقط داخل کانتینر روی 127.0.0.1 گوش می‌دهد.
5. ساعت سرور باید درست باشد (TOTP به زمان وابسته است): `timedatectl status` و NTP داخلی یا `chrony` با منبع داخلی.

## بررسی سلامت

```sh
docker compose exec zerogram dotnet /app/Ferrite.dll --healthcheck && echo OK
docker compose exec zerogram sh -c 'cat /dev/null' >/dev/null    # کانتینر زنده است
docker compose logs --tail 50 zerogram
```

خروجی `healthz` (داخل کانتینر) فقط شمار پیام‌های در صف و عمر قدیمی‌ترین را نشان می‌دهد؛ هیچ نام کاربری یا متن پیامی در آن نیست.

## تنظیمات

همه در `.env` هستند (فهرست کامل در `.env.example`):

| متغیر | پیش‌فرض | معنی |
| --- | --- | --- |
| `ZEROGRAM_PUBLIC_ADDRESS` | — (اجباری) | آدرسی که کلاینت‌ها می‌زنند |
| `ZEROGRAM_OWNER_PASSWORD` | تصادفی (install.sh) | رمز صاحب سرور، حداقل ۱۲ نویسه. اگر خالی باشد ورود صاحب سرور غیرفعال است |
| `ZEROGRAM_SERVER_NAME` | Zerogram | نامی که کاربران برای این سرور می‌بینند (نه IP) |
| `ZEROGRAM_BOOTSTRAP_PORT_HOST` | 5223 | پورت دریافت کلید عمومی برای برنامهٔ صاحب سرور؛ برابر «پورت اصلی + ۱» |
| `ZEROGRAM_QUEUE_TTL_HOURS` | 168 (۷ روز) | انقضای پیام تحویل‌نشده |
| `ZEROGRAM_QUEUE_MAX_MESSAGES_PER_USER` | 10000 | سقف تعداد پیام در صف هر گیرنده |
| `ZEROGRAM_QUEUE_MAX_BYTES_PER_USER` | 67108864 | سقف حجم صف هر گیرنده (۶۴ مگابایت) |
| `ZEROGRAM_QUEUE_MAX_TOTAL_BYTES` | 4294967296 | سقف حجم کل صف (۴ گیگابایت) |
| `ZEROGRAM_GROUP_MEMBER_LIMIT` | 50 | سقف اعضای گروه |
| `ZEROGRAM_MAX_REGISTRATIONS_PER_IP` | 30 در ساعت | محدودیت ثبت‌نام برای هر IP (کاربران پشت NAT اپراتور یک IP مشترک دارند؛ عدد را کم نکنید) |
| `ZEROGRAM_MAX_ATTEMPTS_PER_IP` | 300 در ۱۰ دقیقه | محدودیت تلاش ورود برای هر IP (دفاع اصلی در برابر حدس زدن، قفل هر نام کاربری است) |

وقتی صف گیرنده پر باشد، **ارسال جدید رد می‌شود** (فرستنده خطا می‌بیند و پیام را «ارسال‌نشده» نگه می‌دارد)؛ پیامی که یک بار پذیرفته شده، بی‌صدا حذف نمی‌شود، مگر با انقضای ۷روزه.

## نکتهٔ IP واقعی کاربر

محدودیت‌های هر IP به IP مقصدِ اتصال تکیه دارند. با `ports:` در حالت bridge معمولاً IP واقعی کاربر حفظ می‌شود؛ اگر در `docker compose logs` همه از یک IP (دروازهٔ Docker) دیده می‌شوند، `network_mode: host` را در `docker-compose.yml` بگذارید (و `ports:` را حذف کنید).

</div>
