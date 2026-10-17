"""Owner-side helpers: the mutual password proof and the connection-message codec (mirrors the app)."""
import base64, hashlib, hmac, json, os, struct
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from . import zg

CONNECT_KEY = hashlib.sha256(b"zerogram-connect-message-v1/shared-obfuscation-key").digest()


import pathlib
WORDS = (pathlib.Path(__file__).resolve().parents[3] / 'Ferrite.Services/Zerogram/bip39-english.txt').read_text().split()
assert len(WORDS) == 2048


def normalize_mnemonic(text):
    """Returns the normalized 12-word phrase, or None when a word is unknown or the checksum is wrong."""
    parts = text.lower().split()
    if len(parts) != 12 or any(p not in WORDS for p in parts):
        return None
    bits = ''.join(f'{WORDS.index(p):011b}' for p in parts)
    entropy = int(bits[:128], 2).to_bytes(16, 'big')
    if bits[128:] != f'{hashlib.sha256(entropy).digest()[0]:08b}'[:4]:
        return None
    return ' '.join(parts)


def mnemonic_from_entropy(entropy):
    bits = ''.join(f'{b:08b}' for b in entropy) + f'{hashlib.sha256(entropy).digest()[0]:08b}'[:4]
    return ' '.join(WORDS[int(bits[i:i + 11], 2)] for i in range(0, 132, 11))


def owner_key(mnemonic):
    """K = HMAC-SHA256(BIP39 seed, "zerogram/owner/v1")  (the seed is PBKDF2-HMAC-SHA512, 2048 rounds)."""
    m = normalize_mnemonic(mnemonic)
    assert m, 'not a valid 12-word phrase'
    seed = hashlib.pbkdf2_hmac('sha512', m.encode(), b'mnemonic', 2048, 64)
    return hmac.new(seed, b'zerogram/owner/v1', hashlib.sha256).digest()


def proof(key, label, client_nonce, server_nonce, fingerprint):
    return hmac.new(key, label.encode() + client_nonce + server_nonce + struct.pack('<Q', fingerprint & (2**64 - 1)),
                    hashlib.sha256).digest()


def open_connect_message(message):
    text = ''.join(message.split())
    assert text.startswith('zg1.')
    raw = base64.urlsafe_b64decode(text[4:] + '=' * (-len(text[4:]) % 4))
    plain = AESGCM(CONNECT_KEY).decrypt(raw[:12], raw[12:], b'zg-connect')
    return json.loads(plain)


async def owner_login(client, mnemonic, fingerprint, tamper_proof=False):
    """Runs the challenge/response; verifies the server's proof like the app does."""
    cn = os.urandom(16)
    ch = await client(zg.OwnerHello(cn))
    key = owner_key(mnemonic)
    expected = proof(key, 'zg-owner-srv', cn, ch.server_nonce, fingerprint)
    if not hmac.compare_digest(expected, ch.server_proof):
        raise ValueError('server proof mismatch')
    client_proof = proof(key, 'zg-owner-cli', cn, ch.server_nonce, fingerprint)
    if tamper_proof:
        client_proof = bytes([client_proof[0] ^ 1]) + client_proof[1:]
    return await client(zg.OwnerLogin(cn, ch.server_nonce, client_proof))


def fingerprint_of_pkcs1(der):
    import rsa
    from telethon.crypto import rsa as trsa
    return trsa._compute_fingerprint(rsa.PublicKey.load_pkcs1(der, 'DER'))
