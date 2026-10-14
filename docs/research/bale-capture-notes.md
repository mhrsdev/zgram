# Bale web client: capture notes (partial)

Source: first-pass notes from the capture agent run on the owner's own test accounts, 2026-10-14.
No phone numbers, codes, cookies or tokens are recorded here. Raw HAR / JS bundles are NOT in this repo.

## Confirmed by observation (DevTools only, nothing decoded)
- Web client: `https://web.bale.ai/`, login route `/login?redirectTo=/`.
- Realtime transport is **WebSocket**: `wss://next-ws.bale.ai/ws/`, upgrade answered `101 Switching Protocols`, **binary** frames both ways, including 4-byte frames that are probably heartbeats. Several `ws/` connections were open at once.
- Login uses plain HTTPS Fetch calls named **`StartPhoneAuth`** (-> code screen) and **`ValidateCode`**, both HTTP 200. No 2FA step was observed (the test account may not use one).
- After login the client opens `/chat`; first chat load = 219 requests, ~8.3 MB transferred.
- Sending works from the UI: a short Persian text and a 3000-character text were both accepted (so the real limit is >= 3000 characters; maximum still unknown). The 3000-char send needed the send button (Enter left the text in the composer).
- Incoming messages appeared live while the WebSockets were open (frame not yet correlated).
- A `metrics` Fetch request fires on actions (probably telemetry, not messaging).
- Static bundles seen on first load: `index.<hash>.js`, `Login.<hash>.js` plus numbered chunks (5064, 6229, 4065, 6875, 3759, 2820, 7833, 1078, 8574).

## Not captured yet (blocking the Bale transport)
- HAR (sanitized), copies of the JS bundles, source maps, raw WebSocket frames.
- REST host/paths, content types, how the session token is created and refreshed.
- History fetch, reply / edit / react / delete, file upload payloads; maximum message length.
- Whether the frames are protobuf, and the field layout of send-message / update messages.

## Next step
Saved bundle files (especially `index.*.js` and the chunks that define the API client) are the fastest way to the message definitions; the names `StartPhoneAuth` / `ValidateCode` suggest a service/method-style API whose request and response types are likely defined in those bundles.

## Update (second report, same day)
- WebSocket handshake advertises `permessage-deflate` (frames may be compressed by the WebSocket layer; a client library must negotiate it). Origin header is `https://web.bale.ai`.
- Seen: recurring 4-byte frames (probably heartbeat) and a 7-byte inbound frame. Meaning unverified.
- Composer accepted 4096 and 8192 characters; server delivery of those was not confirmed, so the real limit is still unknown (>= 3000 confirmed).
- UI actions done on test accounts: reply, edit, heart reaction, delete (local only), small image, small text file. Wire payloads for all of these are still unknown.
- Still missing and BLOCKING: JS bundles / source maps. Without them there is no way to learn the message schemas. Use `tools/har_extract_js.py` on a HAR exported with content (it keeps only public .js/.map files, never cookies or tokens).
