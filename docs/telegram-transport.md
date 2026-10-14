# Telegram transport (crates/telegram)

Built on TDLib via `tdlib-rs`. Login = normal Telegram flow (phone, code, optional 2FA password).

## Credentials
One `api_id` / `api_hash` per *app*, not per user. Get it once at https://my.telegram.org
("API development tools"; app title must not contain "Telegram"). Provide at build time:

    export ZEROGRAM_TG_API_ID=123456
    export ZEROGRAM_TG_API_HASH=...

Never commit them to a public repo (Telegram rate-limits published keys: API_ID_PUBLISHED_FLOOD).
"Advanced" mode lets a user pass their own with `ApiCredentials::custom`.

## Build
- Default feature `download`: the build script downloads a prebuilt TDLib from GitHub releases.
- If that is blocked (e.g. TLS-intercepting proxy), download the zip yourself and use
  `--no-default-features --features local` with `LOCAL_TDLIB_PATH=<folder containing lib/ and include/>`.
  At runtime `libtdjson.so` must be on `LD_LIBRARY_PATH` (or next to the executable).

## Status
Compiles and unit-tests the state mapping. NOT yet tested against real Telegram servers
(needs a real api_id and network access). Not yet connected to the Flutter app
(flutter_rust_bridge is the planned glue).
