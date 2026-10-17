# Licenses and notices

Zerogram combines separately licensed works. Their licenses are preserved; nothing here relicenses them.

| Component | Where | License |
| --- | --- | --- |
| Ferrite server (Aykut Alparslan KOÇ) and Zerogram changes to it | this repository (root, `Ferrite*/`) | GNU AGPL-3.0-or-later — see `LICENSE`, `COPYING.txt` |
| `Ferrite.Transport` files derived from ASP.NET Core | `Ferrite.Transport/` | MIT — see `Ferrite.Transport/LICENSE.aspnetcore` |
| Telegram for Android (Telegram FZ-LLC / DrKLO) with Zerogram patches | built from upstream + `client/android/patches/` | GNU GPL-2.0-or-later (upstream `LICENSE`) |
| Otp.NET | NuGet dependency of the server | MIT |
| Microsoft.Data.Sqlite / SQLitePCLRaw | NuGet dependency of the server | MIT / Apache-2.0 (SQLite itself is public domain) |
| RocksDB, FASTER, Lucene.NET, BouncyCastle, SkiaSharp, Autofac, … | NuGet dependencies inherited from Ferrite | their respective licenses (MIT/Apache-2.0/BSD) |
| Telethon (tests only, not shipped) | `tools/e2e`, `tools/load` use it from PyPI | MIT |

Zerogram is independent software. It is not affiliated with, endorsed by, or connected to Telegram Messenger Inc.
or Telegram FZ-LLC. "Telegram" is their trademark. The protocol client libraries inside the Android app keep their
original copyright and license headers.

Because the server is AGPL-3.0, anyone who runs a modified version and lets users interact with it over a network
must offer those users the corresponding source of that modified version. The Android client is GPL-2.0-or-later:
distribute its corresponding source (the pinned upstream commit plus `client/android/patches/`) with every APK.

Alternate launcher icons and some drawable resources inherited from the upstream Android project still carry
Telegram artwork; replace them before distributing under a different public brand.
