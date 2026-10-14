# Telegram Desktop sign-in and account flow, and how to rebuild it on TDLib

Source studied (read-only, shallow clone of the official client): `Telegram/SourceFiles/intro/*`,
`main/main_domain*`, `main/main_account*`, `window/window_main_menu*`, `window/window_controller*`,
`settings/sections/settings_information.cpp` (account list), `tray_accounts_menu*`, `window/notifications_manager*`,
`boxes/phone_banned_box*`, `Resources/langs/lang.strings`. Everything below is a paraphrase in our own words.
Items marked **(?)** are things that were not verifiable in the desktop source and come from general TDLib
knowledge. Verify them against the `td_api.tl` of the TDLib version we pin.

---

## 1. Overall architecture of the desktop intro

* The whole sign-in UI is one container widget (`Intro::Widget`) that holds a **stack of "steps"**
  (`StartWidget`, `QrWidget`, `PhoneWidget`, `EmailWidget`, `CodeWidget`, `PasswordCheckWidget`, `SignupWidget`).
  Each step is a full-window page with a title, a description, its own fields and an optional error line.
* Transitions are three kinds: **Forward** (push), **Back** (pop and destroy the top step), **Replace**
  (swap the top step, used when the old step must not be reachable by Back, e.g. code -> password).
* Shared state between steps lives in one `Data` struct: country, phone (digits only), phone-code-hash,
  code length, "code was delivered through Telegram" flag, Fragment URL, call-me-timer state, email pattern(s),
  cloud-password state, and the Terms of Service blob. In a TDLib port this is just "the last
  `authorizationState` object", so a step is a pure function of the current auth state plus a little
  UI-only state (country, timers).
* Chrome shared by all steps (outside the step page): **Back arrow** (top-left, only on steps that opt in,
  see below), **Settings** button (top-right, hidden on the cover/start page), a **"Test Mode"** label,
  an **Update** button (when an update is ready), a **Next/Submit** round button centered under the form,
  a **"switch language"** link under the button, a **"Reset your account"** button (pinned to the bottom,
  only after the 2FA reset path), a **ToS** label (pinned to the bottom, only on sign-up), and a
  **connection-state** indicator (connecting / proxy).
* Keyboard: Enter, Return and Space trigger the current step's submit; Escape or the hardware Back key
  triggers Back if the step has a back arrow. (In the code field Space moves to the next digit instead.)
* Back behavior (`backRequested`): if the stack has more than one step, pop; otherwise, if the intro was
  opened *from* an already authorized account ("account before intro"), return to that account; otherwise if
  any authorized account exists, activate it; otherwise go to the Start page.
* On every Back/Replace/finish the leaving step runs `cancelled()` which cancels its in-flight request; the code step
  additionally tells the server to cancel the pending login code (`auth.cancelCode`).

### Entry point
When an account slot has no session, the window shows the intro. **If at least one other authorized
account exists, the intro starts directly on the QR page** (skipping the welcome page); otherwise it starts on
the **Start** page. A "nearest data center" request is made on the Start entry point and may update the
default country for the phone page (this is a server-side hint; irrelevant for TDLib).

---

## 2. Screen-by-screen state machine

```
                       +-------------+  "Start Messaging"   +---------+
   first run  ------>  |   Start     | -------------------> |   QR    | <---- entry when another account exists
                       +-------------+                      +----+----+
                                                  "Log in using  |  ^  "Quick log in using QR code"
                                                   phone number" v  |
                                                           +--------+--+
                                                           |  Phone    |
                                                           +----+------+
                    email setup required (rare)  <-------------/ | sentCode
                    +--------+                                   v
                    | Email  | -----------------------------> +--------+
                    +--------+      email code sent            |  Code  | -- wrong/expired code -> error / Back to Phone
                                                               +---+----+
                              SESSION_PASSWORD_NEEDED  <--------/ | \ sign-up required
                              +----------------+                  |  +-----------+
                              | Password (2FA) | <--- (also from QR)  |  Sign-up  |
                              +-------+--------+                  |  +-----+-----+
                                      |                           v        |
                                      +---------------------> authorized <-+
```

### 2.1 Start (welcome / cover page)
* Layout: full-width gradient **cover** with the paper-plane logo, title = app name, description =
  "Welcome to the official Telegram Desktop app. It's fast and secure.", one big **"Start Messaging"** button.
  Settings/Update/Test labels are hidden on the cover page.
* Action: Next -> (also destroys stale auth keys for the account) -> push **QR**.
* No back arrow. This is the only step with a cover (cover collapses with an ease-out animation when moving on).

### 2.2 QR login
* Layout: centered QR image (white rounded plate, the Telegram logo disc in the middle; while a new token
  is loading a circular spinner overlays the logo), title "Scan From Mobile Telegram", a **3-step numbered
  list** ("1. Open Telegram on your phone", "2. Go to Settings > Devices > Link Desktop Device",
  "3. Scan this image to Log In"), a link **"Log in using phone number"** and, when the server config and
  OS allow, a second link **"Log in using passkey"**. No Next button (its text is empty so it hides).
* QR payload is the URL `tg://login?token=<base64url token>`; the image is generated at a fixed module size
  and scaled down to a maximum.
* Token lifecycle: ask the server for a login token; schedule a refresh at `expires - now` (at least 1 s);
  a server push "login token updated" (the phone scanned it) triggers an immediate re-export which will answer
  either `success` (authorized), `migrateTo(dc, token)` (switch main DC and import the token there), or a new
  token. If a refresh arrives while another request is in flight it is queued ("force refresh").
* Errors: `SESSION_PASSWORD_NEEDED` -> fetch password info and **Replace** with the 2FA page. Any other error
  is shown as raw text in the centered error line below the QR; if a forced refresh was pending, retry instead.
* Leaving the page stops refreshing; coming back restarts it.
* Navigation: the "phone number" link pushes the Phone step (or pops back to it if it already is below the QR).

### 2.3 Phone entry
* Layout: title "Your Phone Number", description "Please confirm your country code and enter your phone number.",
  then a **country selector** (dropdown button showing flag/name and dial code), a small **country-code input**
  (starts with `+`) and the **phone-part input** next to it. Below: a link "Quick log in using QR code".
  Next button = "Next" (generic `lng_intro_next`). Back arrow: none on this step as implemented (the Back
  *path* exists only via the stack from QR).
* Number formatting: the phone-part input is a masked field fed with the country's grouping pattern (e.g. groups
  like `XXX XXX XX XX`), so typing auto-inserts spaces. Typing a code in the code input selects the matching
  country; picking a country fills the code input; backspace at the start of the phone part erases the code
  digits; overflow digits typed in the code input are moved into the phone part; Space in the code input submits.
* Default country: when other accounts exist, derived from the other account's phone; otherwise from the
  server "nearest DC" hint; fallback `US`. A user edit stops further automatic country changes.
* Validation (client-side): digits in the full string `+code + number` must be **more than 1**, otherwise the
  "Invalid phone number. Please try again." error appears under the field. If the focus is in the code input,
  it already contains >1 chars and the phone part is empty, Enter just moves focus to the phone part.
* Duplicate detection before sending: if another account **in the same environment (prod/test)** already has the
  same digits as its phone, no request is sent; the app simply **switches to that existing account**.
* Request: send-code with `api_id`, `api_hash` and default code settings. A watchdog timer (1 s) cancels the
  request if it is stuck for 1 s or more in a "sent but no answer" state (lets the user retry).
* Success outcomes:
  * normal `sentCode` -> store phone digits, hash, delivery type -> **Code** page (or **Email** page first when
    the server demands a login email: "setup required").
  * `sentCodeSuccess` (already authorized by other means) -> finish login directly.
  * "payment required" variant -> logged and ignored by desktop (not supported).
* Errors:
  * flood -> "Too many tries. Please try again later."
  * `PHONE_NUMBER_FLOOD` -> modal: "you deleted and re-created your account too many times; wait a few days".
  * `PHONE_NUMBER_INVALID` -> inline "Invalid phone number. Please try again."
  * `PHONE_NUMBER_BANNED` -> modal "This phone number is banned." with **OK** and **Help**; Help opens a
    prefilled `mailto:` to the login-support address with phone, app version, OS and locale in the body.
  * anything else -> raw error text inline.
* Changing either field hides the error.

### 2.4 Login-email setup (rare; only when the server requires it)
* Layout: title "Choose a login email", a description, an animated email icon, an email input, an error line.
  Enter submits.
* Request: send-verify-email-code for purpose "login setup"; answer gives code length and masked email pattern;
  -> push **Code** page (in "email verification" mode).
* Errors: flood (generic), email-not-allowed ("Sorry, this email is not allowed"), invalid ("Invalid email"),
  hash-expired (hard-coded "confirmation expired" text), else raw text. Empty input just shakes/marks the field.

### 2.5 Code entry
* Layout: **title = the formatted phone number** (or "Enter code" for Fragment delivery), a description that
  depends on delivery, a **row of N single-digit boxes** (N = code length from the server, usually 5; up to
  whatever the server says), a secondary line (link or countdown), an error line below it.
  Next button text = "Next" (for Fragment it becomes an accent button "Open Fragment").
* Digit boxes behavior: only digits accepted; arrows/Space/Home/End navigate; Backspace/Delete edit;
  paste and IME commit fill from the focused box onward; **when all N boxes are filled the code is submitted
  automatically** (no button press). Enter submits if complete. A resubmit of the identical code string is
  suppressed. The inputs are disabled while the request is in flight, re-enabled on error.
* Description / delivery variants as the desktop handles them:

  | Delivery type from server | What the UI shows |
  |---|---|
  | **App** (code sent to other Telegram sessions) | "A code was sent via Telegram to your other devices, if you have any connected." plus link **"Send code via SMS"** (calls resend). No call timer. |
  | **SMS** | "We've sent an activation code to your phone. Please enter it below." plus the call countdown line when the *next* delivery is a call. |
  | **Call** (voice) | Same description as SMS (code length from server). |
  | **Fragment SMS** (anonymous numbers) | Title "Enter code", text "Get the code for {phone} in the Anonymous Numbers section on Fragment.", action button **Open Fragment** opens the URL in a browser; the digit boxes are still usable. |
  | **Email code** (login via email) | Description "Please check your email {masked email} (don't forget spam) and enter the code we just sent you." |
  | **Email setup required** | Routes to the Email step first (2.4). |
  | **Flash call / Missed call / Firebase SMS / SMS word / SMS phrase** | **Not supported by desktop**: logged as an API error and treated as "no UI". A TDLib client must decide what to do, see section 4. |

* Resend / call timer: if the *next type* is "call", a countdown "Telegram will call you in M:SS" appears
  (switches to H:MM:SS format at one hour or more) and ticks every second from the server-provided timeout
  (default 60 s if absent). On zero, it automatically requests **resend** and the label changes to
  "Requesting a call from Telegram..." and then, once the server confirms, "Telegram dialed your number".
  There is no manual "resend" for SMS on desktop except the "Send code via SMS" link in the app-delivery case.
  The timer state is persisted in the shared data, so going Back/Forward doesn't reset the countdown.
* Auto-fill: the account can receive a login code pushed from the same app (service message from Telegram on
  another device of this client); when that callback fires, the digits are filled and submitted.
* Submit: sign-in with phone, hash and code (or email-verification code in email-login mode; or verify-email
  in the setup mode, which then produces a *new* sent-code for the phone and **Replace**s the page).
* Errors:
  * flood -> "Too many tries. Please try again later." (field re-enabled and refocused)
  * `PHONE_CODE_INVALID` / `PHONE_CODE_EMPTY` -> inline "You have entered an invalid code."
  * `PHONE_CODE_EXPIRED`, `PHONE_NUMBER_INVALID`, `PHONE_NUMBER_BANNED` -> silently **go Back** to the phone
    page (user must request a new code). No dedicated message in this step.
  * `SESSION_PASSWORD_NEEDED` -> fetch password info and **Replace** with 2FA page. (If the server says a
    password exists but returns no algorithm, a modal "your app is out of date, please update" appears.)
  * sign-up required (authorization answer variant) -> **Replace** with Sign-up page, carrying ToS.
  * anything else -> raw error text.
* Leaving the page cancels the pending code on the server.

### 2.6 2FA password ("Cloud password check")
* Layout, mode A (password): title "Cloud password check", description "Please enter your cloud password.",
  a **password input** (masked) labelled "Password", optional hint line "Hint: {hint}" (hidden if empty),
  link **"Forgot password?"**. Next button text = "Submit".
* Layout, mode B (recovery by email): the password input and hint are hidden; a **code input** appears with the
  description "Please enter the code from the email {masked email}", and the link changes to a "try to remember
  the password" style link (`lng_signin_try_password`, text not captured here) that offers the reset path.
* Password check: client computes the SRP proof locally from the password and server-provided SRP params;
  one retry is allowed if the server says the SRP id is stale (a re-fetch is allowed at most once per minute, otherwise
  a hard "server error" text).
* Errors:
  * flood -> "Too many tries..." and field marked.
  * wrong password (`PASSWORD_HASH_INVALID`, `SRP_PASSWORD_CHANGED`) -> "You have entered a wrong password."
    (text selected so the user can retype).
  * `PASSWORD_EMPTY` / `AUTH_KEY_UNREGISTERED` -> Back.
  * anything else -> raw text.
* "Forgot password?":
  * if the account **has a recovery email**: switch to mode B and request a recovery email; the server returns the
    masked email address. Code check; wrong code -> "You have entered an invalid code."; `PASSWORD_RECOVERY_NA`
    -> back to mode A silently; `PASSWORD_RECOVERY_EXPIRED` -> mode A. A correct code opens a **"set new password"
    box** (the same box as in Settings, with "from recovery code" set); on success the login completes. If Telegram
    Passport data exists a confirm box warns it will be lost first.
  * if there is **no recovery email**: info box: "Since you didn't provide a recovery email... remaining options are
    remember the password or reset your account." After closing it the bottom **"Reset your account"** button
    appears (the language link hides).
* Reset account (bottom button): confirm "You will lose all your chats... Reset?" with a red **Reset** button.
  Then the server's delete-account call with reason "Forgot password" is sent without a password. Outcomes:
  success -> if no phone is known go to QR (Replace/Back animation) else go to **Sign-up** (Replace);
  `2FA_CONFIRM_WAIT_<seconds>` -> info box: account is protected, it will be deleted in 1 week, "you'll be able to
  reset in: X days Y hours Z minutes"; `2FA_RECENT_CONFIRM` -> info: "your recent attempts to reset have been
  canceled by the active user; try in 7 days"; other -> raw text on the step.
* The QR flow reuses this same screen when the export-token call answers `SESSION_PASSWORD_NEEDED`.

### 2.7 Sign-up
* Shown when the server says the number is not registered ("sign up required"), or after an account reset.
* Layout: title "Your Info", description "Please enter your name and upload a photo.", **profile photo picker**
  (round button on the right of the name fields; opens crop dialog, photo is **uploaded after** the session is
  created, not sent with sign-up), **First name** and **Last name** inputs (stacked; order is swapped for locales
  where the family name goes first). Next button text = "Start Messaging" (`lng_intro_finish`).
  The **ToS label** "By signing up, you agree to the Terms of Service." is pinned at the bottom (link opens
  the ToS in an info box with OK only).
* Validation: both names must be non-empty after trimming. Enter in the first field jumps to the last one.
  Name fields are trimmed before sending. No client-side max length beyond server validation.
* ToS acceptance: if the server returned a ToS with the "popup" flag, pressing Next shows an **Agree & Continue /
  Decline** dialog first; Decline shows "Unfortunately, this means you can't sign up for Telegram..." with
  options to go back and agree or dismiss. Only after Agree the sign-up request is sent.
* Errors: flood message; `FIRSTNAME_INVALID` / `LASTNAME_INVALID` -> "Enter your first and last name." and focus
  moves to that field; `PHONE_NUMBER_FLOOD` -> modal about too many deleted/created accounts;
  `PHONE_NUMBER_INVALID`, `PHONE_NUMBER_BANNED`, `PHONE_CODE_EXPIRED`/`EMPTY`/`INVALID`, `PHONE_NUMBER_OCCUPIED`
  -> go Back (code step); else raw text.

### 2.8 Completion (all paths)
1. Server answers an authorization object containing the self user. Desktop sanity-checks it really is "self".
2. **Duplicate account check**: if some other account in the same environment already has that user id, the
   just-finished slot is logged out and the app activates the existing account instead.
3. Load chat folders (best-effort) so the main window can open with correct filters; create the session,
   persist MTProto data, upload the sign-up photo if chosen, refresh app config, switch UI from intro to main window.

### 2.9 Timings summary
| What | Value |
|---|---|
| Stuck-request watchdog (phone and code steps) | polls each 1 s; cancels request if unanswered >= 1 s in the "sent" state |
| Call-me countdown | server `timeout` seconds, default 60, tick 1 s |
| QR token refresh | `expires - now`, minimum 1 s; immediate on "token updated" push |
| SRP-id-invalid refetch throttle | 60 s |
| Reset-account wait | 7 days (server), shown as days/hours/minutes |
| Step slide / cover animations | cosmetic (~hundreds of ms), not required |

---

## 3. Mapping to TDLib

TDLib models sign-in as a linear **authorization state machine**. The UI should subscribe to
`updateAuthorizationState` and render by state; user actions are TDLib functions. Names are from the public
`td_api.tl`; anything marked **(?)** should be re-checked against the pinned version.

| Desktop screen / transition | TDLib state to render | TDLib function(s) to call | Notes |
|---|---|---|---|
| (app start, no TDLib yet) | `authorizationStateWaitTdlibParameters` | `setTdlibParameters(use_test_dc, database_directory, files_directory, use_file_database, use_chat_info_database, use_message_database, use_secret_chats, api_id, api_hash, system_language_code, device_model, system_version, application_version)` | Newer TDLib has this as a function; older versions used `checkDatabaseEncryptionKey` afterward **(?)** (state `WaitEncryptionKey`). Database dir = per-account directory (section 5). |
| Start/welcome page | any of WaitPhoneNumber (before the user chose) | none | Purely local UI. Desktop's "Start Messaging" only navigates to QR. |
| Phone page: submit | `authorizationStateWaitPhoneNumber` | `setAuthenticationPhoneNumber(phone_number, settings: phoneNumberAuthenticationSettings{allow_flash_call, allow_missed_call, is_current_phone_number, has_unknown_phone_number, allow_sms_retriever_api, firebase_authentication_settings, authentication_tokens})` | Pass only digits plus leading `+` optional. Desktop uses default settings (no flash/missed call). To mirror it, send all flags false. |
| Phone page: duplicate-account shortcut | n/a | none | App-level logic: before calling TDLib, compare normalised phone with other accounts' `user.phone_number`. |
| Phone page: QR link | `WaitPhoneNumber` | `requestQrCodeAuthentication(other_user_ids: [])` | Allowed only in state `WaitPhoneNumber`. Moves state to `authorizationStateWaitOtherDeviceConfirmation(link)`. |
| QR page (display) | `authorizationStateWaitOtherDeviceConfirmation { link }` | none | Render `link` (it is a `tg://login?token=...`) as a QR. TDLib refreshes the token itself and emits a **new** `WaitOtherDeviceConfirmation` update with a fresh `link` before expiry, so the UI just re-renders. Desktop's own export/import/migrate logic is hidden inside TDLib. |
| QR page: "login by phone" | `WaitOtherDeviceConfirmation` | `setAuthenticationPhoneNumber(...)` (TDLib allows leaving QR mode by sending a phone number) **(?)** | If it is rejected in your version, call `logOut`/`close`+`destroy` and recreate the client. |
| QR success | next state is `WaitPassword` (2FA) or `Ready` | none | Same as other paths. |
| Login-email setup page | `authorizationStateWaitEmailAddress { allow_apple_id, allow_google_id }` | `setAuthenticationEmailAddress(email_address)` | Error mapping below. |
| Email code page | `authorizationStateWaitEmailCode { code_info: emailAddressAuthenticationCodeInfo{email_address_pattern, length}, email_address_reset_state }` | `checkAuthenticationEmailCode(code: emailAddressAuthenticationCode{code} \| apple/google id token)`, `resendAuthenticationCode(reason)` **(?)**, `resetAuthenticationEmailAddress` | `email_address_reset_state` drives a "can't access email" link (not in desktop). |
| Code page (SMS/call/app/etc.) | `authorizationStateWaitCode { code_info: authenticationCodeInfo{phone_number, type, next_type, timeout} }` | `checkAuthenticationCode(code)`; `resendAuthenticationCode(reason: null)` | See delivery-type table below. |
| Code page: "Send code via SMS" | `WaitCode` with type `authenticationCodeTypeTelegramMessage` | `resendAuthenticationCode()` | Only valid when `next_type != null`. |
| Code page: call countdown/auto call | `WaitCode`, `next_type == authenticationCodeTypeCall`, `timeout` | `resendAuthenticationCode()` when the local timer reaches zero (TDLib itself rejects it if called too early) | Show `timeout` countdown; after the call the new `WaitCode` update has a new `type`/`timeout`. |
| Code page: Back (change number) | `WaitCode` | `logOut()` then re-init, or **(?)** `setAuthenticationPhoneNumber` again is *not* allowed in `WaitCode`; the supported way is to recreate the client (`close` then `destroy`-or-delete the db) | Desktop's "back to phone" cancels the code; with TDLib the equivalent is restarting the auth session. Document this as a product constraint. |
| Code expired | `WaitCode` -> error `PHONE_CODE_EXPIRED` | restart auth as above | Desktop silently goes back to the phone page. Better UX: show "code expired" and restart. |
| Wrong code | `WaitCode` stays | error `PHONE_CODE_INVALID` (400) | Show "invalid code". |
| 2FA page | `authorizationStateWaitPassword { password_hint, has_recovery_email_address, has_passport_data, recovery_email_address_pattern }` | `checkAuthenticationPassword(password)` | SRP is done inside TDLib (the desktop's hash logic disappears). |
| 2FA: "Forgot password?" with recovery email | `WaitPassword`, `has_recovery_email_address = true` | `requestAuthenticationPasswordRecovery`, then `checkAuthenticationPasswordRecoveryCode(code)` and `recoverAuthenticationPassword(recovery_code, new_password, new_hint)` | In TDLib, recovery returns `Ready` after `recoverAuthenticationPassword` (the "new password box" of desktop becomes mandatory fields: new password + hint, both may be empty to remove the password) **(?)**. Show the masked email from `recovery_email_address_pattern` or from `emailAddressAuthenticationCodeInfo` returned by the request. |
| 2FA: no recovery email | `WaitPassword`, `has_recovery_email_address = false` | none for reset; TDLib has **no** pre-auth "delete account" | The desktop's "Reset your account" (delete-account without password) is not exposed as a TDLib function **(?)**. Options: open Telegram's official web reset page / instruct user to use another session, or drop the reset button in v1. |
| Sign-up page | `authorizationStateWaitRegistration { terms_of_service: termsOfService{text, min_user_age, show_popup} }` | `registerUser(first_name, last_name, disable_notification)` | `show_popup` decides whether to show Agree/Decline before calling. Photo: after `Ready`, call `setProfilePhoto(inputChatPhotoStatic{photo: inputFileLocal})`. |
| ToS text viewer | `WaitRegistration.terms_of_service` | none | `text` is a `formattedText`; first and last name max length is 64 in TDLib **(?)**. |
| Premium purchase required (not in desktop) | `authorizationStateWaitPremiumPurchase` **(?)** | `checkAuthenticationPremiumPurchase`, `setAuthenticationPremiumPurchaseTransaction` **(?)** | Only on some platforms/numbers; show "not supported, use the official app". |
| Completion | `authorizationStateReady` | `getMe()`, `loadChats`, etc. | Then run duplicate detection (same `user.id` already in another account of the same messenger). |
| Logout | `authorizationStateLoggingOut` -> `Closing` -> `Closed` | `logOut()` (server-side terminate + wipe) | Different from `close()` which only stops the client and keeps the db. |
| App exit | `Closing` -> `Closed` | `close()` | Wait for `Closed` before dropping the client object. |
| Flood error | request fails | error `code = 429`, message like `Too Many Requests: retry after N` (also `FLOOD_WAIT_N`) **(?)** | Show "Too many tries" plus the N seconds when available. Desktop doesn't show the time, but TDLib gives it, so show it. |
| Banned number | request fails | error 400 `PHONE_NUMBER_BANNED` | Modal with OK/Help (mailto to support). |
| Invalid number | request fails | error 400 `PHONE_NUMBER_INVALID` | Inline error. |
| Number flood | request fails | error 400 `PHONE_NUMBER_FLOOD` | Modal. |
| Wrong/expired/empty code | request fails | 400 `PHONE_CODE_INVALID` / `PHONE_CODE_EXPIRED` / `PHONE_CODE_EMPTY` | As in table. |
| Wrong password | request fails | 400 `PASSWORD_HASH_INVALID` | Inline error. |
| Wrong recovery code | request fails | 400 `CODE_INVALID` (message may differ by TDLib, `PASSWORD_RECOVERY_...`) **(?)** | Inline error. |
| Bad name | request fails | 400 `FIRSTNAME_INVALID` / `LASTNAME_INVALID` | Inline error and focus. |
| Bad email | request fails | 400 `EMAIL_INVALID`, `EMAIL_NOT_ALLOWED`, `EMAIL_HASH_EXPIRED` | Inline error. |

### Delivery types: TDLib `authenticationCodeType` vs desktop behavior

| TDLib type | Desktop behavior | Recommended for the TDLib client |
|---|---|---|
| `authenticationCodeTypeTelegramMessage {length}` | "via Telegram" text + "Send code via SMS" link | Same. |
| `authenticationCodeTypeSms {length}` | SMS text | Same. |
| `authenticationCodeTypeCall {length}` | text as SMS | "We are calling you; the code is spoken". |
| `authenticationCodeTypeFlashCall {pattern}` | unsupported | Needs `allow_flash_call` plus reading call log (mobile only). Don't enable. |
| `authenticationCodeTypeMissedCall {phone_number_prefix, length}` | unsupported | Show "You'll get a missed call from +prefix...; enter the last N digits". Optional. |
| `authenticationCodeTypeFragment {url, length}` | button "Open Fragment" | Same. |
| `authenticationCodeTypeFirebaseAndroid/Ios` | unsupported | Mobile only. Don't enable (leave `firebase_authentication_settings` null). |
| `authenticationCodeTypeSmsWord {first_letter}` / `SmsPhrase {first_word}` | unsupported | Show hint with the first letter/word and a free-text field **(?)**. |
| (email) `authenticationCodeTypeEmail*` no: handled via `WaitEmailCode` state | | |

TDLib limits: you can call `resendAuthenticationCode` only while `next_type != null`, and not before the server
`timeout` elapsed; otherwise it errors.

### Things TDLib does for you (so we must not re-implement them)
* DC selection / migration, token refresh for QR, SRP password math, `auth.cancelCode`, key destruction, config.
* Persisting the session in `database_directory`.

### Things the app layer must still own
* Country picker and phone formatting (use `getCountries` + `getPhoneNumberInfoSync` for formatting and country
  detection **(?)**, or a bundled libphonenumber data set).
* Timers (countdown display), UI step stack, error text mapping, duplicate-account detection, ToS dialog.
* Mapping `updateAuthorizationState` to the UI (the UI should be *driven by state*, never by "I just sent X").

---

## 4. Multi-account in Telegram Desktop

### 4.1 Model
* `Main::Domain` is the single owner of a list of `Main::Account` objects (each with an integer **index**).
  An account may exist **without a session** (a slot waiting in the intro screen) or with one (logged in).
* Limits: **3 accounts** normally. Each logged-in **Premium** account (or any test-server account) raises the cap by
  one, up to a hard maximum of **6**. `maxAccounts = min(premiumCount + 3, 6)`. The value is recomputed when a
  session's premium status changes. When the limit is hit, "Add account" opens a "limit reached" box with the
  premium upsell (rows for the 3rd..6th accounts shown locked in the switcher's reorder list).
* Exactly one account is **active** per window. Optionally an account can be shown in its **own separate window**
  (Ctrl+click on the account row, middle click, or the context menu "Open in new window"). Per-window
  "main account" bookkeeping handles the rest.
* Environment: each account is bound to Production or Test servers (test only via a hidden Alt+Shift right click
  menu on "Add account"). Duplicate detection only compares accounts within the same environment.

### 4.2 Storage (per account)
* One data directory root (`tdata`). Account 0 uses the base name; account *n* (n >= 1) uses the suffix `#n+1`
  (so `#2`, `#3` ...). Each account has its own MTProto keys, config, cached users/chats/media, drafts, settings
  (`Main::SessionSettings`) and file caches. A domain-level file lists which account indexes exist and which one
  was active last (written lazily, "scheduleWriteAccounts"). The optional local **passcode** is domain-wide and
  protects all accounts.
* When an account logs out its local data is wiped and its slot is removed (unless a separate window still shows it).
  When the last logged-in account logs out, the passcode is removed too.
* The account display order in the switcher is a global setting: a list of session unique ids, edited by drag
  and drop in the switcher.

### 4.3 Add / switch / remove
* **Add**: "Add Account" button (in the main-menu accounts list, in Settings top-bar menu, in Settings > Information).
  It first drops empty (session-less) accounts, reuses an existing session-less slot if one exists, else creates
  a new slot (copying the DC config of the active account), activates it and the window shows the intro.
  Since another account exists, the intro starts on **QR**; Back from the first step returns to the account you came from
  (remembered as "account before intro"). Ctrl+click opens the new slot in a separate window.
* **Switch**: click on a row in the account list (main menu), a tray-menu entry (only when >= 2 are logged in; the tray
  shows each account's name truncated to 30 chars plus its userpic; Ctrl+click -> separate window), or the Settings
  list. Switching animates and the previously active session sends "offline" while the new sends "online".
  Clicking the current account just closes the menu.
* **Remove**: row context menu "Log out" (only for non-active accounts there; for the active one use Settings
  > Log out). Shows a confirm box "Are you sure you want to log out?" (extended with warnings about wallet loss, running
  data exports, uploads, downloads; each can add a stop-confirmation). Then the server logout is requested; when it
  completes the session is destroyed, local data reset, and another account is activated automatically.
  The server may also force-log-out an account (auth key revoked): a non-dismissible info box is shown, then it
  logs out locally.
* Also: duplicate login detection (phone typed or user id after auth) -> switch to the existing account, not an error.

### 4.4 UI elements of the switcher
* Main (hamburger) menu header: big userpic of the active account; click on it or on the **arrow toggle** next to the name
  expands/collapses the accounts list (state remembered in app settings; auto-collapses when only one account remains;
  auto-expands the first time a second account is added).
* Collapsed arrow shows a **combined unread badge of the other accounts** (muted ones omitted; hidden if all muted).
* Expanded list: for every logged-in account a row with round userpic, name, a small unread counter (muted counters
  grey), drag handles for reorder; rows beyond the cap are locked with a premium lock; the last row is
  **"Add Account"** with a round "+" icon. Row context menu: Open in new window, Copy phone, Use this account,
  Mark all chats as read, Log out. Accounts with a locked passcode hide some entries.
* Window title/tray/dock badge show the **sum of unread across all accounts**.

### 4.5 Notifications per account
* Every account/session produces notifications independently. When more than one account is logged in, the
  notification title gets a suffix with an arrow and the receiving account's username (or display name if no username)
  so you know which account got the message. Clicking a notification activates the right account (found via the session
  unique id) and opens the chat. A notification is skipped for the *currently open* chat in the *active* account only.
* Online status is only sent for the active account.

---

## 5. Design note: Telegram + Bale in one switcher

Bale is a separate messenger (its own backend and its own protocol; not available through TDLib). So we need an
**account abstraction** above the transport and a **per-account auth driver**.

### 5.1 Account model
```
Account {
  id            : local uuid (stable, used for storage directory)
  provider      : "telegram" | "bale"        // messenger kind
  state         : unauthorized | authorizing(step) | ready | loggingOut | error
  profile       : { displayName, username?, phone?, avatar? }   // known once ready
  unread        : { count, muted }           // fed by provider events
  storageDir    : <app data>/accounts/<id>/  // TDLib database_directory for telegram; own db for bale
}
```
* Accounts are keyed by `(provider, remote user id)` for duplicate detection, and by normalised phone only as a
  pre-auth hint (the same phone number may legitimately exist on both Telegram and Bale: do **not** treat that as a duplicate across providers).
* Cap: the desktop limit of 3/6 is Telegram-Premium-specific. We define our own `maxAccounts` (config), counted across
  providers.

### 5.2 Provider interface (what the sign-in UI talks to)
Define a neutral auth state machine that both providers map to, derived from the TDLib one:
```
AuthStep = Start
         | PhoneNumber{ countryHint }
         | Qr{ link }                       // optional per provider (Telegram yes; Bale: check, may be absent)
         | Code{ target, length, delivery[], nextDelivery?, timeoutSec?, canResend }
         | EmailSetup | EmailCode{ pattern, length }
         | Password{ hint?, recoveryPattern?, canReset }
         | Register{ tosText, showPopup }
         | Ready{ profile } | Failed{ errorKey, retryAfterSec? }
```
Provider methods: `submitPhone(digits)`, `requestQr()`, `submitCode(code)`, `resendCode()`, `submitPassword(pw)`,
`startRecovery()`, `submitRecoveryCode()`, `register(first,last)`, `cancel()`, plus an event stream of `AuthStep`.
* `TelegramProvider`: one TDLib client per account, `updateAuthorizationState` -> `AuthStep` (table in section 3).
* `BaleProvider`: implements the same interface over Bale's own API (phone + SMS/OTP code; whether it has 2FA, QR or
  sign-up in the same shape is unknown, so unsupported steps are simply never emitted). Needs its own spec. Any step a
  provider doesn't support simply never appears.

### 5.3 UI behavior
* The **intro/sign-in screens are shared** (country picker, code boxes, password field, error line) and **parameterized by provider**:
  title/branding, country list defaults (Bale is Iran-centric: default `+98`), allowed delivery types, ToS URL, error-string mapping.
* **"Add account" flow**: the button opens a small **provider chooser** (Telegram / Bale) first, unless only one provider
  is enabled. Then the new slot enters the provider's auth flow starting at the phone step; each provider asks
  for **its own phone number and its own code** with no shared state between slots (a different `Data` object per slot,
  exactly like desktop's per-slot `Data`). Starting on QR (desktop's rule when another account exists) should only apply
  to providers that support QR; for Bale start on the phone page.
* **Switcher rows**: userpic with a small **provider badge** (Telegram plane / Bale logo) so two entries for the same
  person are distinguishable; name; unread counter; the global unread sum in the collapsed header badge should count both
  providers; tray menu lists both.
* **Notifications**: reuse the "title + arrow + account name" scheme, adding the provider badge/name when the same display name appears twice.
* **Switching** keeps both clients running in the background (TDLib client and Bale connection) so unread counters and
  notifications keep working; only the active account's chat UI is mounted. Online-presence only for the active one.
* **Logout/remove**: Telegram -> TDLib `logOut`, wait for `Closed`, delete the directory. Bale -> its own token revocation, delete its db.
  Confirmation text is provider-specific.
* **Persistence**: an app-level `accounts.json` listing `{id, provider, lastActive, order}`; secrets/tokens stay in each provider's own storage. Passcode (if any) is app-wide.
* **Errors** are normalised to keys (`phone_invalid`, `phone_banned`, `code_invalid`, `code_expired`, `flood{seconds}`, `password_invalid`,
  `name_invalid`, ...) in the provider layer, so one set of UI strings serves both providers.

### 5.4 Open questions to resolve before building
1. Which Bale API/SDK is used (official Bot API is not a user login; a user client needs the protocol/auth flow, to be confirmed legally and technically).
2. Whether Bale supports QR login, 2FA password and call delivery. The shared step model makes each optional.
3. Whether TDLib needs `use_test_dc` toggle in the UI (desktop hides it behind Alt+Shift).
4. Passkey login (offered by desktop on the QR page when server config allows) has **no TDLib API that we know of (?)**. Skip in v1.
