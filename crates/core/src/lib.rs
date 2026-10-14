//! Zerogram core: everything that does not depend on a specific messenger.
//!
//! Pipeline for sending:  `Event -> postcard -> zstd -> XChaCha20-Poly1305 -> chunks -> CJK text`
//! Pipeline for receiving is the exact inverse, followed by insertion into an [`EventLog`].

pub mod codec;
pub mod crypto;
pub mod event;
pub mod log;
pub mod wire;

pub use crypto::GroupKey;
pub use event::{Event, EventId, EventKind, GroupId, Hlc, SenderId};
pub use log::{EventLog, Message};
pub use wire::{Assembler, Sealed};

#[derive(Debug, thiserror::Error)]
pub enum Error {
    #[error("decryption failed (wrong group key or corrupted data)")]
    Decrypt,
    #[error("malformed data: {0}")]
    Malformed(&'static str),
    #[error("serialization: {0}")]
    Serde(#[from] postcard::Error),
    #[error("compression: {0}")]
    Io(#[from] std::io::Error),
}

pub type Result<T> = std::result::Result<T, Error>;
