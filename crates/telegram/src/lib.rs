//! Telegram transport on top of TDLib (the official Telegram library).
//!
//! Login is the normal Telegram flow: phone -> code -> optional 2FA password.
//! Credentials: one `api_id`/`api_hash` belongs to the whole Zerogram app and
//! is baked in at build time (`ZEROGRAM_TG_API_ID` / `ZEROGRAM_TG_API_HASH`),
//! so users never have to create their own. Advanced users can supply their own.
//! Never commit the values to a public repository.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use tdlib_rs::enums::{self, AuthorizationState, MessageContent, Update};
use tdlib_rs::{functions, types};
use tokio::sync::mpsc;

#[derive(Clone, Debug)]
pub struct ApiCredentials {
    pub api_id: i32,
    pub api_hash: String,
}

impl ApiCredentials {
    /// The app-wide credentials compiled into this build, if any.
    pub fn embedded() -> Option<Self> {
        let id = option_env!("ZEROGRAM_TG_API_ID")?.trim().parse().ok()?;
        let hash = option_env!("ZEROGRAM_TG_API_HASH")?.trim().to_string();
        (!hash.is_empty()).then_some(Self { api_id: id, api_hash: hash })
    }

    /// Credentials entered by the user (the "advanced" login mode).
    pub fn custom(api_id: i32, api_hash: impl Into<String>) -> Self {
        Self { api_id, api_hash: api_hash.into() }
    }
}

/// Neutral login state, mirrors `AuthStep` in the Flutter app.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum AuthState {
    WaitPhone,
    WaitCode { phone: String, length: i32 },
    WaitPassword { hint: String },
    /// New number: Telegram needs a first/last name.
    WaitRegistration,
    /// Telegram asks for e-mail verification (not implemented in the UI yet).
    WaitEmail,
    /// Another device must approve the login (QR-style link).
    WaitOtherDevice { link: String },
    Ready,
    Closed,
}

#[derive(Clone, Debug)]
pub struct IncomingMessage {
    pub chat_id: i64,
    pub message_id: i64,
    pub text: String,
    pub outgoing: bool,
    pub date: i32,
}

#[derive(Clone, Debug)]
pub struct ChatSummary {
    pub id: i64,
    pub title: String,
    pub last: Option<IncomingMessage>,
}

#[derive(Debug)]
pub enum Event {
    Auth(AuthState),
    NewMessage(IncomingMessage),
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TgError {
    pub code: i32,
    pub message: String,
}

impl From<types::Error> for TgError {
    fn from(e: types::Error) -> Self {
        Self { code: e.code, message: e.message }
    }
}

type Res<T> = Result<T, TgError>;

/// Pure mapping from a TDLib authorization state; `None` for states that need
/// no UI (e.g. WaitTdlibParameters, LoggingOut).
pub fn map_auth_state(s: &AuthorizationState) -> Option<AuthState> {
    Some(match s {
        AuthorizationState::WaitPhoneNumber => AuthState::WaitPhone,
        AuthorizationState::WaitCode(c) => AuthState::WaitCode {
            phone: c.code_info.phone_number.clone(),
            length: code_length(&c.code_info.r#type),
        },
        AuthorizationState::WaitPassword(p) => AuthState::WaitPassword { hint: p.password_hint.clone() },
        AuthorizationState::WaitRegistration(_) => AuthState::WaitRegistration,
        AuthorizationState::WaitEmailAddress(_) | AuthorizationState::WaitEmailCode(_) => AuthState::WaitEmail,
        AuthorizationState::WaitOtherDeviceConfirmation(x) => AuthState::WaitOtherDevice { link: x.link.clone() },
        AuthorizationState::Ready => AuthState::Ready,
        AuthorizationState::Closed => AuthState::Closed,
        _ => return None,
    })
}

fn code_length(t: &enums::AuthenticationCodeType) -> i32 {
    use enums::AuthenticationCodeType as T;
    match t {
        T::TelegramMessage(x) => x.length,
        T::Sms(x) => x.length,
        _ => 5,
    }
}

fn to_incoming(m: &types::Message) -> IncomingMessage {
    let text = match &m.content {
        MessageContent::MessageText(t) => t.text.text.clone(),
        // Non-text content is shown as a placeholder until media is supported.
        _ => String::new(),
    };
    IncomingMessage { chat_id: m.chat_id, message_id: m.id, text, outgoing: m.is_outgoing, date: m.date }
}

pub struct TelegramClient {
    client_id: i32,
    running: Arc<AtomicBool>,
}

impl TelegramClient {
    /// Starts TDLib and returns a handle plus a stream of events.
    /// `data_dir` holds this account's local database (one folder per account).
    pub fn start(creds: ApiCredentials, data_dir: String) -> (Self, mpsc::UnboundedReceiver<Event>) {
        let client_id = tdlib_rs::create_client();
        let running = Arc::new(AtomicBool::new(true));
        let (tx, rx) = mpsc::unbounded_channel();

        // TDLib's receive() blocks, so it gets its own thread.
        let run = running.clone();
        std::thread::spawn(move || {
            let handle = tokio::runtime::Builder::new_current_thread().enable_all().build().unwrap();
            while run.load(Ordering::Acquire) {
                let Some((update, cid)) = tdlib_rs::receive() else { continue };
                if cid != client_id {
                    continue;
                }
                match update {
                    Update::AuthorizationState(u) => {
                        if matches!(u.authorization_state, AuthorizationState::WaitTdlibParameters) {
                            let (c, d) = (creds.clone(), data_dir.clone());
                            handle.block_on(async move {
                                let _ = functions::set_tdlib_parameters(
                                    false, d, String::new(), String::new(),
                                    true, true, true, false,
                                    c.api_id, c.api_hash, "en".into(), "Desktop".into(),
                                    String::new(), env!("CARGO_PKG_VERSION").into(), client_id,
                                ).await;
                            });
                        } else if let Some(s) = map_auth_state(&u.authorization_state) {
                            let closed = s == AuthState::Closed;
                            let _ = tx.send(Event::Auth(s));
                            if closed {
                                run.store(false, Ordering::Release);
                            }
                        }
                    }
                    Update::NewMessage(u) => {
                        let _ = tx.send(Event::NewMessage(to_incoming(&u.message)));
                    }
                    _ => {}
                }
            }
        });

        (Self { client_id, running }, rx)
    }

    pub async fn set_phone(&self, phone: &str) -> Res<()> {
        functions::set_authentication_phone_number(phone.into(), None, self.client_id).await.map_err(Into::into)
    }
    pub async fn check_code(&self, code: &str) -> Res<()> {
        functions::check_authentication_code(code.into(), self.client_id).await.map_err(Into::into)
    }
    pub async fn check_password(&self, password: &str) -> Res<()> {
        functions::check_authentication_password(password.into(), self.client_id).await.map_err(Into::into)
    }
    pub async fn register(&self, first: &str, last: &str) -> Res<()> {
        functions::register_user(first.into(), last.into(), false, self.client_id).await.map_err(Into::into)
    }

    /// Chats of the main list, with their last message.
    pub async fn chats(&self, limit: i32) -> Res<Vec<ChatSummary>> {
        // load_chats fails with 404 once everything is loaded; that is fine.
        let _ = functions::load_chats(Some(enums::ChatList::Main), limit, self.client_id).await;
        let enums::Chats::Chats(list) = functions::get_chats(Some(enums::ChatList::Main), limit, self.client_id).await?;
        let mut out = Vec::new();
        for id in list.chat_ids {
            let enums::Chat::Chat(c) = functions::get_chat(id, self.client_id).await?;
            out.push(ChatSummary { id, title: c.title, last: c.last_message.as_ref().map(to_incoming) });
        }
        Ok(out)
    }

    /// Latest `limit` messages, oldest first.
    pub async fn history(&self, chat_id: i64, limit: i32) -> Res<Vec<IncomingMessage>> {
        let enums::Messages::Messages(m) =
            functions::get_chat_history(chat_id, 0, 0, limit, false, self.client_id).await?;
        let mut v: Vec<_> = m.messages.iter().flatten().map(to_incoming).collect();
        v.reverse();
        Ok(v)
    }

    pub async fn send_text(&self, chat_id: i64, text: &str) -> Res<IncomingMessage> {
        let content = enums::InputMessageContent::InputMessageText(types::InputMessageText {
            text: types::FormattedText { text: text.into(), entities: vec![] },
            link_preview_options: None,
            clear_draft: true,
        });
        let enums::Message::Message(m) =
            functions::send_message(chat_id, None, None, None, content, self.client_id).await?;
        Ok(to_incoming(&m))
    }

    pub async fn log_out(&self) -> Res<()> {
        functions::log_out(self.client_id).await.map_err(Into::into)
    }

    pub async fn close(&self) -> Res<()> {
        let r = functions::close(self.client_id).await.map_err(Into::into);
        self.running.store(false, Ordering::Release);
        r
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn custom_credentials_keep_values() {
        let c = ApiCredentials::custom(12345, "abc");
        assert_eq!((c.api_id, c.api_hash.as_str()), (12345, "abc"));
    }

    #[test]
    fn password_state_maps_with_hint() {
        let s = AuthorizationState::WaitPassword(types::AuthorizationStateWaitPassword {
            password_hint: "pet".into(),
            has_recovery_email_address: false,
            has_passport_data: false,
            recovery_email_address_pattern: String::new(),
        });
        assert_eq!(map_auth_state(&s), Some(AuthState::WaitPassword { hint: "pet".into() }));
    }

    #[test]
    fn simple_states_map() {
        assert_eq!(map_auth_state(&AuthorizationState::WaitPhoneNumber), Some(AuthState::WaitPhone));
        assert_eq!(map_auth_state(&AuthorizationState::Ready), Some(AuthState::Ready));
        assert_eq!(map_auth_state(&AuthorizationState::WaitTdlibParameters), None);
    }
}
