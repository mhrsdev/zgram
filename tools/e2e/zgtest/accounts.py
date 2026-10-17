import time
import pyotp
from . import zg
from .helper import make_client


def code_for(secret, step_offset=0):
    """TOTP code of the current 30 s step shifted by whole steps (server accepts +-window)."""
    return pyotp.TOTP(secret).at(time.time() + 30 * step_offset)


async def new_account(server, username, name=None, email=None, activate=True):
    c = make_client('127.0.0.1', server.port)
    await c.connect()
    reg = await c(zg.Register(username, name or username.title(), email or f'{username}@example.com'))
    if not activate:
        return c, reg, None
    act = await c(zg.Activate(username, code_for(reg.manual_key)))
    return c, reg, act


def input_peer(user):
    from telethon.tl import types
    return types.InputPeerUser(user.id, user.access_hash)
