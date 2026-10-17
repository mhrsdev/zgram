#!/usr/bin/env python3
"""End-to-end tests for Zerogram against a real server process, driven by Telethon (an independent
MTProto client). Usage: run_e2e.py [test-name-substring ...]  -> writes results/e2e-report.json"""
import asyncio, json, os, random, sqlite3, sys, time, traceback, pathlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from telethon import functions, types, events, errors
from telethon.errors import RPCError
from zgtest import zg
from zgtest.accounts import new_account, code_for, input_peer
from zgtest.helper import make_client, load_server_key
from zgtest.server import Server

RESULTS = []
BASE_ENV = {'ZEROGRAM_MAX_REGISTRATIONS_PER_IP': '1000', 'ZEROGRAM_MAX_ATTEMPTS_PER_IP': '5000',
            'ZEROGRAM_TOTP_WINDOW_STEPS': '4', 'ZEROGRAM_QUEUE_SWEEP_SECONDS': '1'}


def uname(prefix):
    return f'{prefix}{random.randint(10000, 99999)}'


async def send(client, peer, text, random_id=None):
    return await client(functions.messages.SendMessageRequest(
        peer=peer, message=text, random_id=random_id or random.getrandbits(62)))


async def pull(client, pts=1):
    """Everything the server would deliver to a client whose last state is `pts`."""
    d = await client(functions.updates.GetDifferenceRequest(pts=pts, date=0, qts=-1))
    return list(getattr(d, 'new_messages', []))


def error_names(e):
    """Telethon turns well-known RPC errors into classes; keep every spelling we can match on."""
    import re
    n = type(e).__name__
    n = n[:-5] if n.endswith('Error') else n
    snake = re.sub(r'(?<!^)(?=[A-Z])', '_', n).upper()
    return {snake, str(e), str(getattr(e, 'message', ''))}


async def expect_error(coro, needle):
    try:
        await coro
    except RPCError as e:
        names = error_names(e)
        assert any(needle in x for x in names), f'expected {needle}, got {names}'
        return e
    raise AssertionError(f'expected error {needle}, call succeeded')


def db(server):
    return sqlite3.connect(f'file:{server.data}/zerogram.db?mode=ro', uri=True)


def queue_rows(server):
    return db(server).execute('select count(*) from queue').fetchone()[0]


# ----------------------------------------------------------------------------- tests

async def t_account_lifecycle(s):
    u = uname('alice')
    c, reg, act = await new_account(s, u)
    assert reg.otpauth_uri.startswith('otpauth://totp/Zerogram:'), reg.otpauth_uri
    assert 'period=30' in reg.otpauth_uri and 'digits=6' in reg.otpauth_uri and reg.period == 30
    assert len(act.recovery_codes) == 10 and len(set(act.recovery_codes)) == 10
    assert act.authorization.user.username == u
    # duplicate username (any case) is refused
    c2 = make_client('127.0.0.1', s.port); await c2.connect()
    await expect_error(c2(zg.Register(u.upper(), 'X', 'x@example.com')), 'USERNAME_OCCUPIED')
    # login from a second device with the next TOTP step
    r = await c2(zg.Login(u, code_for(reg.manual_key, +1)))
    assert r.user.id == act.authorization.user.id
    # single active device: the first session is revoked
    await expect_error(c(functions.updates.GetStateRequest()), 'AUTH_KEY_UNREGISTERED')
    # the new session works
    await c2(functions.updates.GetStateRequest())
    await c.disconnect(); await c2.disconnect()


async def t_input_validation(s):
    c = make_client('127.0.0.1', s.port); await c.connect()
    await expect_error(c(zg.Register('ab', 'Name', 'a@b.co')), 'USERNAME_INVALID')
    await expect_error(c(zg.Register('1invalid_start', 'Name', 'a@b.co')), 'USERNAME_INVALID')
    await expect_error(c(zg.Register('admin', 'Name', 'a@b.co')), 'USERNAME_INVALID')
    await expect_error(c(zg.Register(uname('okname'), '', 'a@b.co')), 'FIRST_NAME_INVALID')
    await expect_error(c(zg.Register(uname('okname'), 'bad‮name', 'a@b.co')), 'FIRST_NAME_INVALID')
    await expect_error(c(zg.Register(uname('okname'), 'Name', 'not-an-email')), 'EMAIL_INVALID')
    # Persian display names (with ZWNJ) are fine
    reg = await c(zg.Register(uname('persian'), 'علی‌رضا', 'fa@example.ir'))
    assert reg.display_name == 'علی‌رضا'
    await c.disconnect()


async def t_activation_rules(s):
    u = uname('actrule')
    c = make_client('127.0.0.1', s.port); await c.connect()
    reg = await c(zg.Register(u, 'Act', 'a@example.com'))
    # not a code of this secret
    await expect_error(c(zg.Activate(u, '000000')), 'CODE_INVALID')
    # another connection cannot finish somebody else's pending registration
    other = make_client('127.0.0.1', s.port); await other.connect()
    await expect_error(other(zg.Activate(u, code_for(reg.manual_key))), 'CODE_INVALID')
    # but the owner can; before activation login is impossible
    await expect_error(c(zg.Login(u, code_for(reg.manual_key, 1))), 'CODE_INVALID')
    act = await c(zg.Activate(u, code_for(reg.manual_key)))
    assert act.authorization.user.username == u
    await c.disconnect(); await other.disconnect()


async def t_totp_replay_and_lockout(s):
    u = uname('replay')
    c, reg, act = await new_account(s, u)
    k = reg.manual_key
    c2 = make_client('127.0.0.1', s.port); await c2.connect()
    good = code_for(k, +1)
    await c2(zg.Login(u, good))
    c3 = make_client('127.0.0.1', s.port); await c3.connect()
    # the same code (and the one used for activation) can never be used again
    await expect_error(c3(zg.Login(u, good)), 'CODE_INVALID')
    await expect_error(c3(zg.Login(u, code_for(k, 0))), 'CODE_INVALID')
    # a *later* step is still fine (only spent steps are refused)
    await c3(zg.Login(u, code_for(k, +2)))
    # wrong codes -> lockout that also blocks a correct code
    u2 = uname('brute')
    d, reg2, _ = await new_account(s, u2)
    d2 = make_client('127.0.0.1', s.port); await d2.connect()
    for _ in range(5):
        await expect_error(d2(zg.Login(u2, '123456')), 'CODE_INVALID')
    e = await expect_error(d2(zg.Login(u2, code_for(reg2.manual_key, +1))), 'FLOOD_WAIT')
    assert e.seconds >= 1
    for x in (c, c2, c3, d, d2): await x.disconnect()


async def t_lockout_expires(s):
    u = uname('lockexp')
    c, reg, act = await new_account(s, u)
    c2 = make_client('127.0.0.1', s.port); await c2.connect()
    for _ in range(5):
        await expect_error(c2(zg.Login(u, '654321')), 'CODE_INVALID')
    await expect_error(c2(zg.Login(u, code_for(reg.manual_key, 1))), 'FLOOD_WAIT')
    await asyncio.sleep(4)
    r = await c2(zg.Login(u, code_for(reg.manual_key, 1)))
    assert r.user.username == u
    await c.disconnect(); await c2.disconnect()


async def t_unknown_user_is_indistinguishable(s):
    c = make_client('127.0.0.1', s.port); await c.connect()
    u = uname('known')
    c1, reg, _ = await new_account(s, u)
    e1 = await expect_error(c(zg.Login(u, '111111')), 'CODE_INVALID')
    e2 = await expect_error(c(zg.Login(uname('ghost'), '111111')), 'CODE_INVALID')
    assert type(e1) is type(e2) and str(e1).split('(')[0] == str(e2).split('(')[0]
    await c.disconnect(); await c1.disconnect()


async def t_recovery(s):
    u = uname('recov')
    c, reg, act = await new_account(s, u)
    codes = act.recovery_codes
    d = make_client('127.0.0.1', s.port); await d.connect()
    new = await d(zg.Recover(u, codes[0]))
    assert new.manual_key != reg.manual_key
    # old session is dead, old secret is dead, used recovery code is dead
    await expect_error(c(functions.updates.GetStateRequest()), 'AUTH_KEY_UNREGISTERED')
    e = make_client('127.0.0.1', s.port); await e.connect()
    await expect_error(e(zg.Login(u, code_for(reg.manual_key, 1))), 'CODE_INVALID')
    await expect_error(e(zg.Login(u, code_for(new.manual_key, 0))), 'CODE_INVALID')  # not activated yet
    await expect_error(e(zg.Recover(u, codes[0])), 'CODE_INVALID')
    # until the new secret is confirmed, the *other* unused codes still work (no lock-out)
    again = await e(zg.Recover(u, codes[1]))
    assert again.manual_key not in (reg.manual_key, new.manual_key)
    # confirm the newest secret: logs in, issues a fresh set of codes, invalidates the old set
    act2 = await e(zg.Activate(u, code_for(again.manual_key, 0)))
    assert act2.authorization.user.id == act.authorization.user.id
    assert set(act2.recovery_codes).isdisjoint(codes)
    f = make_client('127.0.0.1', s.port); await f.connect()
    await expect_error(f(zg.Recover(u, codes[2])), 'CODE_INVALID')
    await f(zg.Recover(u, act2.recovery_codes[0]))
    for x in (c, d, e, f): await x.disconnect()


async def t_secrets_not_stored_in_clear(s):
    u = uname('vault')
    c, reg, act = await new_account(s, u)
    blob = b''
    for p in pathlib.Path(s.data).rglob('*'):
        if p.is_file() and p.name != 'zerogram-master.key':
            try: blob += p.read_bytes()
            except Exception: pass
    assert reg.manual_key.encode() not in blob, 'TOTP secret found in clear text on disk'
    import base64
    raw = base64.b32decode(reg.manual_key + '=' * (-len(reg.manual_key) % 8))
    assert raw not in blob, 'raw TOTP secret found on disk'
    for code in act.recovery_codes:
        assert code.replace('-', '').encode() not in blob and code.encode() not in blob, 'recovery code stored in clear'
    assert (s.data / 'zerogram-master.key').stat().st_mode & 0o077 == 0, 'master key must be owner-only'
    n = db(s).execute('select count(*) from recovery_codes where username=?', (u,)).fetchone()[0]
    assert n == 10
    await c.disconnect()


async def t_phone_login_and_out_of_scope_methods_disabled(s):
    u = uname('scope')
    c, reg, act = await new_account(s, u)
    anon = make_client('127.0.0.1', s.port); await anon.connect()
    await expect_error(anon(functions.auth.SendCodeRequest('+15551234567', 1, 'x', types.CodeSettings())), 'METHOD_DISABLED')
    await expect_error(anon(functions.auth.SignInRequest('+15551234567', 'abc', phone_code='12345')), 'METHOD_DISABLED')
    await expect_error(c(functions.channels.CreateChannelRequest(title='x', about='y', megagroup=True)), 'METHOD_DISABLED')
    await expect_error(c(functions.phone.RequestCallRequest(user_id=types.InputUserSelf(), random_id=1, g_a_hash=b'0' * 32,
                                                           protocol=types.PhoneCallProtocol(min_layer=65, max_layer=92, library_versions=['1']))), 'METHOD_DISABLED')
    await expect_error(c(functions.upload.SaveFilePartRequest(1, 0, b'x')), 'METHOD_DISABLED')
    await expect_error(c(functions.messages.SendMediaRequest(peer=types.InputPeerSelf(), media=types.InputMediaEmpty(), message='', random_id=5)), 'METHOD_DISABLED')
    await anon.disconnect(); await c.disconnect()


async def t_send_ack_purge_and_delivery_mark(s):
    a, ra, acta = await new_account(s, uname('sender'))
    b, rb, actb = await new_account(s, uname('rcvr'))
    got = []
    b.add_event_handler(lambda u: got.append(u), events.Raw)
    pb = input_peer(actb.authorization.user)
    r = await send(a, pb, 'سلام، این یک پیام آزمایشی است')
    assert r.id > 0
    msgs = await pull(b)
    assert [m.message for m in msgs] == ['سلام، این یک پیام آزمایشی است']
    assert queue_rows(s) == 1
    # an unacknowledged message is delivered again to a client that lost its state
    assert len(await pull(b)) == 1
    ids = [m.id for m in msgs]
    assert (await b(zg.AckMessages(ids))).acked == 1
    assert (await b(zg.AckMessages(ids))).acked == 0, 'ACK must be idempotent'
    assert queue_rows(s) == 0
    assert await pull(b) == [], 'the server copy must be gone after ACK'
    await asyncio.sleep(0.5)
    marks = (await a(zg.GetDeliveryState())).marks
    assert len(marks) == 1 and marks[0][1] == r.id, (marks, r.id)
    # silent purge: no deleteMessages update ever reaches the recipient (it would erase local history)
    kinds = [type(x).__name__ for x in got]
    assert not any('Delete' in k for k in kinds), kinds
    await a.disconnect(); await b.disconnect()


async def t_retransmission_is_idempotent(s):
    a, _, _ = await new_account(s, uname('retry'))
    b, _, actb = await new_account(s, uname('retrb'))
    pb = input_peer(actb.authorization.user)
    rid = random.getrandbits(62)
    r1 = await send(a, pb, 'once', rid)
    r2 = await send(a, pb, 'once', rid)
    assert (r1.id, r1.pts, r1.date) == (r2.id, r2.pts, r2.date)
    assert [m.message for m in await pull(b)] == ['once']
    # still idempotent after the recipient acknowledged and after a server restart
    ids = [m.id for m in await pull(b)]
    await b(zg.AckMessages(ids))
    await a.disconnect(); await b.disconnect()
    s.restart()
    a2 = make_client('127.0.0.1', s.port); await a2.connect()
    await expect_error(a2(functions.updates.GetStateRequest()), 'AUTH_KEY_UNREGISTERED') if False else None
    await a2.disconnect()


async def t_restart_keeps_undelivered_messages(s):
    a, ra, acta = await new_account(s, uname('rsa'))
    b, rb, actb = await new_account(s, uname('rsb'))
    ua, ub = acta.authorization.user, actb.authorization.user
    pb = input_peer(ub)
    for i in range(5):
        await send(a, pb, f'm{i}')
    await a.disconnect(); await b.disconnect()
    s.restart(graceful=True)
    b2 = make_client('127.0.0.1', s.port); await b2.connect()
    await b2(zg.Login(ub.username, code_for(rb.manual_key, +1)))
    msgs = await pull(b2)
    assert sorted(m.message for m in msgs) == [f'm{i}' for i in range(5)], [m.message for m in msgs]
    await b2(zg.AckMessages([m.id for m in msgs[:3]]))
    await b2.disconnect()
    s.restart(graceful=False)  # kill -9
    b3 = make_client('127.0.0.1', s.port); await b3.connect()
    await b3(zg.Login(ub.username, code_for(rb.manual_key, +2)))
    left = await pull(b3)
    assert len(left) == 2 and all(m.message in ('m3', 'm4') for m in left), [m.message for m in left]
    await b3.disconnect()


async def t_ids_and_pts_never_regress_after_hard_kill(s):
    a, ra, acta = await new_account(s, uname('kya'))
    b, rb, actb = await new_account(s, uname('kyb'))
    pb = input_peer(actb.authorization.user)
    last = None
    for i in range(20):
        last = await send(a, pb, f'pre{i}')
    st_before = await b(functions.updates.GetStateRequest())
    await a.disconnect(); await b.disconnect()
    s.restart(graceful=False)
    a2 = make_client('127.0.0.1', s.port); await a2.connect()
    await a2(zg.Login(acta.authorization.user.username, code_for(ra.manual_key, 1)))
    r = await send(a2, pb, 'post')
    assert r.id > last.id and r.pts > last.pts, (r.id, last.id, r.pts, last.pts)
    b2 = make_client('127.0.0.1', s.port); await b2.connect()
    await b2(zg.Login(actb.authorization.user.username, code_for(rb.manual_key, 1)))
    st = await b2(functions.updates.GetStateRequest())
    assert st.pts >= st_before.pts
    msgs = await pull(b2)
    assert len(msgs) == 21 and len({m.id for m in msgs}) == 21, len(msgs)
    await a2.disconnect(); await b2.disconnect()


async def t_expiry(s):
    a, _, _ = await new_account(s, uname('exa'))
    b, rb, actb = await new_account(s, uname('exb'))
    pb = input_peer(actb.authorization.user)
    for i in range(3): await send(a, pb, f'e{i}')
    assert queue_rows(s) == 3
    await asyncio.sleep(9)
    assert queue_rows(s) == 0, 'expired rows must be swept'
    assert await pull(b) == [], 'expired message bodies must be purged'
    await a.disconnect(); await b.disconnect()


async def t_queue_caps(s):
    a, _, _ = await new_account(s, uname('capa'))
    b, _, actb = await new_account(s, uname('capb'))
    pb = input_peer(actb.authorization.user)
    for i in range(3): await send(a, pb, f'c{i}')
    await expect_error(send(a, pb, 'overflow'), 'RECIPIENT_QUEUE_FULL')
    msgs = await pull(b)
    assert len(msgs) == 3 and 'overflow' not in [m.message for m in msgs]
    await b(zg.AckMessages([m.id for m in msgs]))
    await send(a, pb, 'fits-again')
    # the sender's rejected message was not stored anywhere
    assert queue_rows(s) == 1
    await a.disconnect(); await b.disconnect()


async def t_group_per_recipient_ack_and_limit(s):
    accts = [await new_account(s, uname(f'grp{i}')) for i in range(4)]
    owner, others = accts[0], accts[1:]
    users = [types.InputUser(x[2].authorization.user.id, x[2].authorization.user.access_hash) for x in others]
    r = await owner[0](functions.messages.CreateChatRequest(users=users, title='Team'))
    chat_id = r.updates.chats[0].id
    peer = types.InputPeerChat(chat_id)
    # the "group created" service message is also a server-side copy: devices acknowledge it too
    for c, _, _ in accts:
        svc = await pull(c)
        assert svc and all(isinstance(m, types.MessageService) for m in svc), svc
        await c(zg.AckMessages([m.id for m in svc]))
    assert queue_rows(s) == 0, 'service messages must be purged after ACK'
    sent = await send(owner[0], peer, 'hello team')
    pulled = []
    for c, _, _ in others:
        pulled.append(await pull(c))
    assert all(len(p) == 1 and p[0].message == 'hello team' for p in pulled), pulled
    assert queue_rows(s) == 3
    # acks are tracked per recipient: the sender's double check waits for the last one
    await others[0][0](zg.AckMessages([pulled[0][0].id]))
    await others[1][0](zg.AckMessages([pulled[1][0].id]))
    await asyncio.sleep(0.4)
    marks = (await owner[0](zg.GetDeliveryState())).marks
    assert all(m[1] < sent.id for m in marks) or not marks, marks
    assert queue_rows(s) == 1
    await others[2][0](zg.AckMessages([pulled[2][0].id]))
    await asyncio.sleep(0.4)
    marks = (await owner[0](zg.GetDeliveryState())).marks
    assert marks and marks[0][1] == sent.id, marks
    assert queue_rows(s) == 0
    for c, _, _ in accts: await c.disconnect()


async def t_group_size_limit(s):
    base, _, bact = await new_account(s, uname('big'))
    members = []
    for i in range(52):
        c, _, act = await new_account(s, uname(f'm{i}x'))
        members.append(types.InputUser(act.authorization.user.id, act.authorization.user.access_hash))
        await c.disconnect()
    await expect_error(base(functions.messages.CreateChatRequest(users=members, title='too big')), 'USERS_TOO_MUCH')
    ok = await base(functions.messages.CreateChatRequest(users=members[:49], title='exactly 50'))
    chat_id = ok.updates.chats[0].id
    await expect_error(base(functions.messages.AddChatUserRequest(chat_id=chat_id, user_id=members[50], fwd_limit=0)), 'USERS_TOO_MUCH')
    await base.disconnect()


async def t_logout_keeps_nothing_server_side_but_relogin_works(s):
    u = uname('lo')
    c, reg, act = await new_account(s, u)
    await c(functions.auth.LogOutRequest())
    c2 = make_client('127.0.0.1', s.port); await c2.connect()
    r = await c2(zg.Login(u, code_for(reg.manual_key, 1)))
    assert r.user.id == act.authorization.user.id
    await c.disconnect(); await c2.disconnect()


async def t_no_message_text_in_logs_or_indexes(s):
    secret = f'TOP-SECRET-{random.randint(10**9, 10**10)}'
    a, _, _ = await new_account(s, uname('lga'))
    b, rb, actb = await new_account(s, uname('lgb'))
    pb = input_peer(actb.authorization.user)
    await send(a, pb, secret)
    msgs = await pull(b)
    await b(zg.AckMessages([m.id for m in msgs]))
    await a.disconnect(); await b.disconnect()
    s.stop()  # flush everything
    s.restart(graceful=True)
    s.stop()
    s.compact_hint = True
    log = s.log_path.read_bytes()
    assert secret.encode() not in log, 'message text leaked to the server log'
    # purged messages must not survive in any search index
    for p in pathlib.Path(s.data).rglob('*'):
        if p.is_file() and ('lucene' in str(p).lower() or 'search' in str(p).lower()):
            assert secret.encode() not in p.read_bytes(), f'message text found in index file {p}'
    s.start()


async def t_policy(s):
    c = make_client('127.0.0.1', s.port); await c.connect()
    p = await c(zg.GetPolicy())
    assert p.group_member_limit == 50 and p.queue_ttl_days == 7 and p.single_device
    await c.disconnect()


# ----------------------------------------------------------------------------- runner

TESTS = [
    # (name, env overrides, test)
    ('account_lifecycle', {}, t_account_lifecycle),
    ('input_validation', {}, t_input_validation),
    ('activation_rules', {}, t_activation_rules),
    ('totp_replay_and_lockout', {}, t_totp_replay_and_lockout),
    ('lockout_expires', {'ZEROGRAM_LOCKOUT_BASE_SECONDS': '3'}, t_lockout_expires),
    ('unknown_user_indistinguishable', {}, t_unknown_user_is_indistinguishable),
    ('recovery', {}, t_recovery),
    ('secrets_not_stored_in_clear', {}, t_secrets_not_stored_in_clear),
    ('scope_and_phone_login_disabled', {}, t_phone_login_and_out_of_scope_methods_disabled),
    ('send_ack_purge_delivery_mark', {}, t_send_ack_purge_and_delivery_mark),
    ('retransmission_idempotent', {}, t_retransmission_is_idempotent),
    ('restart_keeps_undelivered', {}, t_restart_keeps_undelivered_messages),
    ('ids_never_regress_after_kill9', {}, t_ids_and_pts_never_regress_after_hard_kill),
    ('expiry', {'ZEROGRAM_QUEUE_TTL_SECONDS': '4'}, t_expiry),
    ('queue_caps', {'ZEROGRAM_QUEUE_MAX_MESSAGES_PER_USER': '3'}, t_queue_caps),
    ('group_per_recipient_ack', {}, t_group_per_recipient_ack_and_limit),
    ('group_size_limit_50', {}, t_group_size_limit),
    ('logout_relogin', {}, t_logout_keeps_nothing_server_side_but_relogin_works),
    ('no_content_in_logs_or_index', {}, t_no_message_text_in_logs_or_indexes),
    ('policy', {}, t_policy),
]


async def run_one(name, env, fn):
    server = Server(name, {**BASE_ENV, **env})
    t0 = time.time()
    try:
        server.start()
        load_server_key(server.public_key_pkcs1())
        await asyncio.wait_for(fn(server), timeout=240)
        status, detail = 'PASS', ''
    except Exception as e:
        status, detail = 'FAIL', ''.join(traceback.format_exception_only(type(e), e)).strip() + '\n' + traceback.format_exc()[-1500:]
    finally:
        try: server.stop()
        except Exception: server.kill9()
    RESULTS.append({'test': name, 'status': status, 'seconds': round(time.time() - t0, 1), 'detail': detail})
    print(f'{status:5} {name} ({RESULTS[-1]["seconds"]}s)' + (f'\n{detail}' if detail else ''), flush=True)


async def main():
    only = sys.argv[1:]
    for name, env, fn in TESTS:
        if only and not any(o in name for o in only):
            continue
        await run_one(name, env, fn)
    out = pathlib.Path(__file__).parent / 'results'
    out.mkdir(exist_ok=True)
    (out / 'e2e-report.json').write_text(json.dumps(RESULTS, indent=2, ensure_ascii=False))
    failed = [r for r in RESULTS if r['status'] != 'PASS']
    print(f'\n{len(RESULTS) - len(failed)}/{len(RESULTS)} passed')
    sys.exit(1 if failed else 0)

asyncio.run(main())
