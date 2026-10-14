use serde::{Deserialize, Serialize};

pub type SenderId = [u8; 8];
pub type GroupId = [u8; 16];

/// Globally unique, never reused: `(sender, per-sender counter)`.
/// Two people sending at the same instant can never collide.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, PartialOrd, Ord, Serialize, Deserialize)]
pub struct EventId {
    pub sender: SenderId,
    pub counter: u64,
}

/// Hybrid logical clock: wall-clock milliseconds in the high bits, a logical
/// counter in the low 16. Gives a total order that survives clock skew.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize)]
pub struct Hlc(pub u64);

impl Hlc {
    pub fn tick(self, now_ms: u64) -> Hlc {
        let phys = now_ms << 16;
        Hlc(if phys > self.0 { phys } else { self.0 + 1 })
    }
    /// Advance past a timestamp seen on an incoming event.
    pub fn observe(self, seen: Hlc, now_ms: u64) -> Hlc {
        Hlc(self.0.max(seen.0)).tick(now_ms)
    }
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub enum EventKind {
    Message { text: String, reply_to: Option<EventId> },
    Edit { target: EventId, text: String },
    Reaction { target: EventId, emoji: String, add: bool },
    Delete { target: EventId },
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub struct Event {
    pub id: EventId,
    pub hlc: Hlc,
    pub kind: EventKind,
}
