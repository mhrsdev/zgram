#!/usr/bin/env python3
"""Closed-network test. Runs inside a network namespace that has ONLY the loopback interface (see
closed_network.sh): no route to the Internet, no DNS, no other host. The server is traced with strace and
every connect() it (and its child processes) makes is recorded afterwards.

Scenario (everything the product needs offline): register + QR/TOTP activation, login on a second
"device", private message online and offline, delivery acknowledgement, graceful restart, kill -9 restart,
reconnect and receive. Pass = all steps work AND the server never tried to connect to a non-loopback
address."""
import asyncio, glob, json, os, random, re, socket, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from telethon import functions
from zgtest import zg
from zgtest.accounts import new_account, code_for, input_peer
from zgtest.helper import make_client, load_server_key
from zgtest.server import Server, WORK

os.environ['ZG_STRACE'] = str(WORK / 'closed-net-connects')


async def pull(c):
    d = await c(functions.updates.GetDifferenceRequest(pts=1, date=0, qts=-1))
    return list(getattr(d, 'new_messages', []))


def external_probe():
    """Prove the namespace really is closed."""
    results = {}
    for name, target in (('tcp_1.1.1.1:443', ('1.1.1.1', 443)), ('tcp_149.154.167.51:443 (telegram dc)', ('149.154.167.51', 443)),
                         ('tcp_142.250.185.78:443 (google)', ('142.250.185.78', 443))):
        s = socket.socket(); s.settimeout(3)
        try:
            s.connect(target); results[name] = 'CONNECTED (namespace is not closed!)'
        except OSError as e:
            results[name] = f'blocked ({e.__class__.__name__}: {e.strerror})'
        finally:
            s.close()
    try:
        socket.getaddrinfo('dns.google', 443); results['dns dns.google'] = 'RESOLVED (not closed!)'
    except OSError as e:
        results['dns dns.google'] = f'blocked ({e.__class__.__name__})'
    return results


async def main():
    report = {'isolation': external_probe(), 'steps': {}}
    assert not any('not closed' in v or 'CONNECTED' in v for v in report['isolation'].values()), report
    s = Server('closed-net', {'ZEROGRAM_TOTP_WINDOW_STEPS': '4', 'ZEROGRAM_MAX_REGISTRATIONS_PER_IP': '1000'})
    s.start()
    load_server_key(s.public_key_pkcs1())

    a, ra, acta = await new_account(s, f'cna{random.randint(1000, 9999)}')
    b, rb, actb = await new_account(s, f'cnb{random.randint(1000, 9999)}')
    report['steps']['register_activate_with_authenticator'] = 'ok'
    pb = input_peer(actb.authorization.user)
    await a(functions.messages.SendMessageRequest(peer=pb, message='online hello', random_id=random.getrandbits(62)))
    got = await pull(b)
    assert [m.message for m in got] == ['online hello']
    await b(zg.AckMessages([m.id for m in got]))
    report['steps']['send_receive_ack'] = 'ok'

    await a.disconnect(); await b.disconnect()
    for i in range(3):   # recipient "offline"
        c = make_client('127.0.0.1', s.port); await c.connect()
        await c(zg.Login(acta.authorization.user.username, code_for(ra.manual_key, 1 + i))) if i == 0 else None
        await c.disconnect()
    a2 = make_client('127.0.0.1', s.port); await a2.connect()
    await a2(zg.Login(acta.authorization.user.username, code_for(ra.manual_key, 2)))
    for i in range(3):
        await a2(functions.messages.SendMessageRequest(peer=pb, message=f'offline {i}', random_id=random.getrandbits(62)))
    report['steps']['send_while_recipient_offline'] = 'ok'
    await a2.disconnect()

    s.restart(graceful=True)
    b2 = make_client('127.0.0.1', s.port); await b2.connect()
    await b2(zg.Login(actb.authorization.user.username, code_for(rb.manual_key, 1)))
    got = await pull(b2)
    assert sorted(m.message for m in got) == ['offline 0', 'offline 1', 'offline 2'], [m.message for m in got]
    await b2(zg.AckMessages([got[0].id]))
    await b2.disconnect()
    report['steps']['graceful_restart_reconnect_receive'] = 'ok'

    s.restart(graceful=False)
    b3 = make_client('127.0.0.1', s.port); await b3.connect()
    await b3(zg.Login(actb.authorization.user.username, code_for(rb.manual_key, 2)))
    got = await pull(b3)
    assert len(got) == 2, [m.message for m in got]
    report['steps']['kill9_restart_reconnect_receive'] = 'ok'
    await b3.disconnect()
    s.stop()

    # what did the server try to connect to?
    targets = {}
    for path in glob.glob(str(WORK / 'closed-net-connects.*')):
        for line in open(path, errors='replace'):
            m = re.search(r'connect\(\d+, \{sa_family=(AF_\w+), (?:sin6?_port=htons\((\d+)\), (?:inet_pton\(AF_INET6?, "([^"]+)"|sin_addr=inet_addr\("([^"]+)"\))|sun_path="([^"]*)")', line)
            if not m:
                continue
            fam, port, a6, a4, path_ = m.groups()
            key = f'{fam}:{a4 or a6 or path_}' + (f':{port}' if port else '')
            targets[key] = targets.get(key, 0) + 1
    external = {k: v for k, v in targets.items() if k.startswith(('AF_INET:', 'AF_INET6:')) and not re.match(r'AF_INET6?:(127\.|::1|0\.0\.0\.0)', k)}
    report['server_connect_targets'] = targets
    report['external_connect_attempts'] = external
    report['result'] = 'PASS' if not external else 'FAIL'
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'results'); os.makedirs(out, exist_ok=True)
    json.dump(report, open(os.path.join(out, 'closed-network.json'), 'w'), indent=2)
    print(json.dumps(report, indent=2))
    sys.exit(0 if report['result'] == 'PASS' else 1)

asyncio.run(main())
