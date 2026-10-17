"""Monkeypatch Telethon to use the RSA_PAD scheme of req_DH_params (the only one Ferrite accepts)."""
import os, hashlib
from telethon.crypto import rsa, AES

def _encrypt(fingerprint, data, *, use_old=False):
    key, old = rsa._server_keys.get(fingerprint, [None, None])
    if not key or (old and not use_old):
        return None
    assert len(data) <= 144
    while True:
        padded = data + os.urandom(192 - len(data))
        rev = padded[::-1]
        temp_key = os.urandom(32)
        with_hash = rev + hashlib.sha256(temp_key + padded).digest()
        aes_enc = AES.encrypt_ige(with_hash, temp_key, b"\0" * 32)
        xor = bytes(a ^ b for a, b in zip(temp_key, hashlib.sha256(aes_enc).digest()))
        block = xor + aes_enc
        v = int.from_bytes(block, 'big')
        if v < key.n:
            return pow(v, key.e, key.n).to_bytes(256, "big")

rsa.encrypt = _encrypt
import telethon.network.authenticator as _a
_a.rsa.encrypt = _encrypt
