//! Bytes <-> text at 14 bits per character (base64 is 6).
//!
//! Characters come from U+4E00..U+8DFF, 16384 consecutive, assigned CJK
//! ideographs in the BMP. Each is one UTF-16 unit, so messenger limits counted
//! in characters (Telegram: 4096) see ~7 bytes per 4 chars instead of 3 per 4.
//! A terminator byte (0x80) is appended before encoding so the decoder can
//! strip padding unambiguously.

use crate::{Error, Result};

const BASE: u32 = 0x4E00;
const BITS: u32 = 14;
const MASK: u32 = (1 << BITS) - 1;

pub fn encode(data: &[u8]) -> String {
    let mut out = String::with_capacity(data.len() * 8 / 14 + 2);
    let (mut acc, mut n) = (0u32, 0u32);
    let emit = |acc: &mut u32, n: &mut u32, out: &mut String| {
        while *n >= BITS {
            *n -= BITS;
            out.push(char::from_u32(BASE + ((*acc >> *n) & MASK)).unwrap());
        }
    };
    for &b in data.iter().chain(std::iter::once(&0x80u8)) {
        acc = (acc << 8) | b as u32;
        n += 8;
        emit(&mut acc, &mut n, &mut out);
        acc &= (1 << n) - 1;
    }
    if n > 0 {
        out.push(char::from_u32(BASE + ((acc << (BITS - n)) & MASK)).unwrap());
    }
    out
}

pub fn decode(text: &str) -> Result<Vec<u8>> {
    let mut out = Vec::with_capacity(text.chars().count() * 14 / 8);
    let (mut acc, mut n) = (0u32, 0u32);
    for c in text.chars() {
        let v = (c as u32).wrapping_sub(BASE);
        if v > MASK { return Err(Error::Malformed("character outside alphabet")); }
        acc = (acc << BITS) | v;
        n += BITS;
        while n >= 8 {
            n -= 8;
            out.push((acc >> n) as u8);
        }
        acc &= (1 << n) - 1;
    }
    while out.last() == Some(&0) { out.pop(); }
    if out.pop() != Some(0x80) { return Err(Error::Malformed("missing terminator")); }
    Ok(out)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn roundtrip_all_lengths() {
        for len in 0..200usize {
            let data: Vec<u8> = (0..len).map(|i| (i * 37 + 11) as u8).collect();
            assert_eq!(decode(&encode(&data)).unwrap(), data, "len {len}");
        }
        // data that itself ends in zeros / 0x80
        for d in [&[0u8, 0][..], &[0x80], &[1, 0x80, 0, 0]] {
            assert_eq!(decode(&encode(d)).unwrap(), d);
        }
    }
    #[test]
    fn denser_than_base64() {
        let data = vec![0xA5u8; 3000];
        let chars = encode(&data).chars().count();
        assert!(chars < 3000 * 4 / 3 * 6 / 10, "{chars}");
    }
    #[test]
    fn rejects_foreign_text() { assert!(decode("hello").is_err()); }
}
