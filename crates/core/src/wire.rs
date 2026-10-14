//! Event <-> list of text messages, one per chunk, each within `max_chars`.
//!
//! Chunk bytes: `msg_tag(6) | index(1) | total(1) | data`. Each chunk is
//! encoded separately, so chunks may arrive in any order or duplicated, and
//! they are independent of which messenger carried them.

use crate::{codec, Error, Event, GroupId, GroupKey, Result};
use rand::RngCore;
use std::collections::HashMap;

const TAG_LEN: usize = 6;
const HEADER: usize = TAG_LEN + 2;

pub struct Sealed;

impl Sealed {
    /// Serialize, compress, encrypt and split into messages of at most `max_chars` characters.
    pub fn pack(event: &Event, group: &GroupId, key: &GroupKey, max_chars: usize) -> Result<Vec<String>> {
        let raw = postcard::to_stdvec(event)?;
        let compressed = zstd::encode_all(&raw[..], 19)?;
        let sealed = key.seal(group, &compressed);
        // bytes per chunk so that encoded length <= max_chars (14 bits/char, +1 terminator byte)
        let per_chunk = (max_chars * 14 / 8).saturating_sub(HEADER + 2);
        if per_chunk == 0 { return Err(Error::Malformed("max_chars too small")); }
        let parts: Vec<&[u8]> = sealed.chunks(per_chunk).collect();
        if parts.len() > 255 { return Err(Error::Malformed("event too large")); }
        let mut tag = [0u8; TAG_LEN];
        rand::rngs::OsRng.fill_bytes(&mut tag);
        Ok(parts.iter().enumerate().map(|(i, p)| {
            let mut b = Vec::with_capacity(HEADER + p.len());
            b.extend_from_slice(&tag);
            b.push(i as u8);
            b.push(parts.len() as u8);
            b.extend_from_slice(p);
            codec::encode(&b)
        }).collect())
    }
}

/// Collects chunks (from any transport) and yields complete events.
#[derive(Default)]
pub struct Assembler {
    pending: HashMap<[u8; TAG_LEN], (u8, HashMap<u8, Vec<u8>>)>,
}

impl Assembler {
    /// Feed one received text. `Ok(None)`: not ours / incomplete / duplicate. `Err`: ours but undecryptable.
    pub fn push(&mut self, text: &str, group: &GroupId, key: &GroupKey) -> Result<Option<Event>> {
        let Ok(b) = codec::decode(text.trim()) else { return Ok(None) };
        if b.len() < HEADER { return Ok(None); }
        let tag: [u8; TAG_LEN] = b[..TAG_LEN].try_into().unwrap();
        let (idx, total) = (b[TAG_LEN], b[TAG_LEN + 1]);
        if total == 0 || idx >= total { return Ok(None); }
        let entry = self.pending.entry(tag).or_insert_with(|| (total, HashMap::new()));
        entry.1.insert(idx, b[HEADER..].to_vec());
        if entry.1.len() < entry.0 as usize { return Ok(None); }
        let (total, mut parts) = self.pending.remove(&tag).unwrap();
        let sealed: Vec<u8> = (0..total).flat_map(|i| parts.remove(&i).unwrap()).collect();
        let compressed = key.open(group, &sealed)?;
        let raw = zstd::decode_all(&compressed[..])?;
        Ok(Some(postcard::from_bytes(&raw)?))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::{EventId, EventKind, Hlc};

    fn ev(text: &str) -> Event {
        Event { id: EventId { sender: [1; 8], counter: 1 }, hlc: Hlc(5), kind: EventKind::Message { text: text.into(), reply_to: None } }
    }

    #[test]
    fn short_message_is_one_chunk_and_roundtrips() {
        let (g, k) = ([7u8; 16], GroupKey::generate());
        let msgs = Sealed::pack(&ev("سلام دنیا"), &g, &k, 4096).unwrap();
        assert_eq!(msgs.len(), 1);
        let got = Assembler::default().push(&msgs[0], &g, &k).unwrap().unwrap();
        assert_eq!(got, ev("سلام دنیا"));
    }

    #[test]
    fn long_message_splits_and_reassembles_out_of_order_with_dupes() {
        let (g, k) = ([7u8; 16], GroupKey::generate());
        let mut rnd = vec![0u8; 5000];
        rand::rngs::OsRng.fill_bytes(&mut rnd);
        let text: String = rnd.iter().map(|b| format!("{b:02x}")).collect(); // incompressible-ish
        let msgs = Sealed::pack(&ev(&text), &g, &k, 500).unwrap();
        assert!(msgs.len() > 3);
        assert!(msgs.iter().all(|m| m.chars().count() <= 500));
        let mut a = Assembler::default();
        let mut out = None;
        for m in msgs.iter().rev().chain(msgs.iter()) {
            if let Some(e) = a.push(m, &g, &k).unwrap() { out = Some(e); break; }
        }
        assert_eq!(out.unwrap(), ev(&text));
    }

    #[test]
    fn other_group_key_cannot_read() {
        let g = [7u8; 16];
        let msgs = Sealed::pack(&ev("secret"), &g, &GroupKey::generate(), 4096).unwrap();
        assert!(Assembler::default().push(&msgs[0], &g, &GroupKey::generate()).is_err());
        // right key, wrong group (AAD) also fails
        let k = GroupKey::generate();
        let msgs = Sealed::pack(&ev("secret"), &g, &k, 4096).unwrap();
        assert!(Assembler::default().push(&msgs[0], &[8u8; 16], &k).is_err());
    }

    #[test]
    fn ordinary_chat_text_is_ignored() {
        let (g, k) = ([7u8; 16], GroupKey::generate());
        assert!(Assembler::default().push("hi there", &g, &k).unwrap().is_none());
    }
}
