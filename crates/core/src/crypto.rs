use crate::{Error, GroupId, Result};
use chacha20poly1305::{aead::{Aead, KeyInit, Payload}, XChaCha20Poly1305, XNonce};
use rand::RngCore;

const NONCE_LEN: usize = 24;

/// Random 256-bit key, independent per group. Leaking one group's key says
/// nothing about any other group. Distribute it out-of-band (QR / invite string).
#[derive(Clone)]
pub struct GroupKey([u8; 32]);

impl GroupKey {
    pub fn generate() -> Self {
        let mut k = [0u8; 32];
        rand::rngs::OsRng.fill_bytes(&mut k);
        Self(k)
    }
    pub fn from_bytes(b: [u8; 32]) -> Self { Self(b) }
    pub fn as_bytes(&self) -> &[u8; 32] { &self.0 }

    /// Output: `nonce(24) || ciphertext+tag`. The group id is authenticated
    /// (AAD), so a ciphertext cannot be replayed into another group.
    pub fn seal(&self, group: &GroupId, plaintext: &[u8]) -> Vec<u8> {
        let mut nonce = [0u8; NONCE_LEN];
        rand::rngs::OsRng.fill_bytes(&mut nonce);
        let ct = XChaCha20Poly1305::new((&self.0).into())
            .encrypt(XNonce::from_slice(&nonce), Payload { msg: plaintext, aad: group })
            .expect("encryption cannot fail for in-memory input");
        [nonce.as_slice(), &ct].concat()
    }

    pub fn open(&self, group: &GroupId, data: &[u8]) -> Result<Vec<u8>> {
        if data.len() < NONCE_LEN + 16 { return Err(Error::Malformed("too short")); }
        let (nonce, ct) = data.split_at(NONCE_LEN);
        XChaCha20Poly1305::new((&self.0).into())
            .decrypt(XNonce::from_slice(nonce), Payload { msg: ct, aad: group })
            .map_err(|_| Error::Decrypt)
    }
}
