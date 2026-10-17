import asyncio, os, sys, time
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from . import rsapad  # noqa: F401  (RSA_PAD monkeypatch)
import telethon.network.connection as conn
from telethon import TelegramClient
from telethon.sessions import MemorySession, SQLiteSession
from telethon.crypto import rsa

def load_server_key(pem_or_path):
    text = pem_or_path if 'BEGIN' in pem_or_path else open(pem_or_path).read()
    rsa.add_key(text, old=False)

def make_client(host='127.0.0.1', port=5222, session=None):
    c = TelegramClient(session or MemorySession(), 12345, 'abcdef', connection=conn.ConnectionTcpObfuscated,
                       device_model='zg-test', app_version='zg-test', lang_code='fa', system_lang_code='fa',
                       flood_sleep_threshold=0, request_retries=1, connection_retries=3)
    c.session.set_dc(1, host, port)
    return c
