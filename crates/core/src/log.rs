use crate::{Event, EventId, EventKind, Hlc};
use std::collections::{BTreeMap, HashMap, HashSet};

/// A message as the UI should show it.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Message {
    pub id: EventId,
    pub hlc: Hlc,
    pub text: String,
    pub reply_to: Option<EventId>,
    pub edited: bool,
    /// emoji -> reacting senders
    pub reactions: BTreeMap<String, HashSet<crate::SenderId>>,
}

/// Append-only set of events. The same event arriving via Telegram, Bale or
/// both is stored once; arrival order is irrelevant to the result.
#[derive(Default)]
pub struct EventLog {
    events: HashMap<EventId, Event>,
}

impl EventLog {
    /// Returns `true` if the event was new.
    pub fn insert(&mut self, e: Event) -> bool {
        self.events.insert(e.id, e.clone()).is_none()
    }

    /// Materialize the conversation: ordered by (hlc, id), with edits,
    /// reactions and deletes applied. Edits/deletes only count when issued by
    /// the original sender; the latest edit (by hlc) wins.
    pub fn view(&self) -> Vec<Message> {
        let mut all: Vec<&Event> = self.events.values().collect();
        all.sort_by_key(|e| (e.hlc, e.id));

        let mut msgs: HashMap<EventId, Message> = HashMap::new();
        let mut order = Vec::new();
        let mut deleted = HashSet::new();
        for e in &all {
            if let EventKind::Message { text, reply_to } = &e.kind {
                order.push(e.id);
                msgs.insert(e.id, Message { id: e.id, hlc: e.hlc, text: text.clone(), reply_to: *reply_to, edited: false, reactions: BTreeMap::new() });
            }
        }
        for e in &all {
            match &e.kind {
                EventKind::Edit { target, text } if target.sender == e.id.sender => {
                    if let Some(m) = msgs.get_mut(target) { m.text = text.clone(); m.edited = true; }
                }
                EventKind::Reaction { target, emoji, add } => {
                    if let Some(m) = msgs.get_mut(target) {
                        let set = m.reactions.entry(emoji.clone()).or_default();
                        if *add { set.insert(e.id.sender); } else { set.remove(&e.id.sender); }
                    }
                }
                EventKind::Delete { target } if target.sender == e.id.sender => { deleted.insert(*target); }
                _ => {}
            }
        }
        order.into_iter().filter(|id| !deleted.contains(id)).filter_map(|id| msgs.remove(&id))
            .map(|mut m| { m.reactions.retain(|_, s| !s.is_empty()); m }).collect()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn id(s: u8, c: u64) -> EventId { EventId { sender: [s; 8], counter: c } }
    fn msg(s: u8, c: u64, h: u64, t: &str) -> Event {
        Event { id: id(s, c), hlc: Hlc(h), kind: EventKind::Message { text: t.into(), reply_to: None } }
    }

    #[test]
    fn dedup_and_order_independent_of_arrival() {
        let evs = vec![msg(1, 1, 10, "a"), msg(2, 1, 20, "b"), msg(1, 2, 30, "c")];
        let mut x = EventLog::default();
        let mut y = EventLog::default();
        for e in &evs { x.insert(e.clone()); }
        for e in evs.iter().rev().chain(evs.iter()) { y.insert(e.clone()); } // reversed + duplicates
        assert_eq!(x.view(), y.view());
        assert_eq!(x.view().iter().map(|m| m.text.as_str()).collect::<Vec<_>>(), ["a", "b", "c"]);
    }

    #[test]
    fn edit_react_delete_rules() {
        let mut l = EventLog::default();
        l.insert(msg(1, 1, 10, "hello"));
        l.insert(Event { id: id(1, 2), hlc: Hlc(11), kind: EventKind::Edit { target: id(1, 1), text: "hello!".into() } });
        l.insert(Event { id: id(2, 1), hlc: Hlc(12), kind: EventKind::Edit { target: id(1, 1), text: "hacked".into() } }); // not the author
        l.insert(Event { id: id(2, 2), hlc: Hlc(13), kind: EventKind::Reaction { target: id(1, 1), emoji: "👍".into(), add: true } });
        let v = l.view();
        assert_eq!(v[0].text, "hello!");
        assert!(v[0].edited);
        assert_eq!(v[0].reactions["👍"].len(), 1);
        l.insert(Event { id: id(1, 3), hlc: Hlc(14), kind: EventKind::Delete { target: id(1, 1) } });
        assert!(l.view().is_empty());
    }

    #[test]
    fn hlc_survives_clock_skew() {
        let local = Hlc::default().tick(1_000);
        let remote = Hlc(5_000 << 16);
        assert!(local.observe(remote, 1_001) > remote);
    }
}
