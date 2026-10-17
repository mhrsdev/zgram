"""Owner-side helpers: the mutual password proof and the connection-message codec (mirrors the app)."""
import base64, hashlib, hmac, json, os, struct
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from . import zg

CONNECT_KEY = hashlib.sha256(b"zerogram-connect-message-v1/shared-obfuscation-key").digest()


def derive_key(password, salt, iterations):
    return hashlib.pbkdf2_hmac('sha256', password.encode(), salt, iterations, 32)


def proof(key, label, client_nonce, server_nonce, fingerprint):
    return hmac.new(key, label.encode() + client_nonce + server_nonce + struct.pack('<Q', fingerprint & (2**64 - 1)),
                    hashlib.sha256).digest()


def open_connect_message(message):
    text = ''.join(message.split())
    assert text.startswith('zg1.')
    raw = base64.urlsafe_b64decode(text[4:] + '=' * (-len(text[4:]) % 4))
    plain = AESGCM(CONNECT_KEY).decrypt(raw[:12], raw[12:], b'zg-connect')
    return json.loads(plain)


async def owner_login(client, password, fingerprint, tamper_proof=False):
    """Runs the challenge/response; verifies the server's proof like the app does."""
    cn = os.urandom(16)
    ch = await client(zg.OwnerHello(cn))
    key = derive_key(password, ch.salt, ch.iterations)
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
