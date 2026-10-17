#!/usr/bin/env python3
"""Checks the real Docker image: durability across `docker restart` (SIGTERM) and `docker kill` (SIGKILL),
volume persistence, owner-only file modes and the container health check.
Requires a running container (default name zerogram-bench, network host, port 5222)."""
import asyncio, json, os, random, re, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from telethon import functions
from zgtest import zg
from zgtest.accounts import new_account, code_for, input_peer
from zgtest.helper import make_client, load_server_key

NAME = os.environ.get('ZG_CONTAINER', 'zerogram-bench')
PORT = int(os.environ.get('ZG_PORT', '5222'))


def sh(*args, check=True):
    return subprocess.run(args, capture_output=True, text=True, check=check).stdout.strip()


def wait_healthy(timeout=120):
    t0 = time.time()
    while time.time() - t0 < timeout:
        st = sh('docker', 'inspect', '-f', '{{.State.Health.Status}}', NAME, check=False)
        if st == 'healthy':
            return time.time() - t0
        time.sleep(1)
    raise TimeoutError('container did not become healthy')


def key_pem():
    logs = subprocess.run(['docker', 'logs', NAME], capture_output=True, text=True).stdout
    pem = re.search(r'-----BEGIN PUBLIC KEY-----.*?-----END PUBLIC KEY-----', logs, re.S).group(0)
    return subprocess.run(['openssl', 'rsa', '-pubin', '-RSAPublicKey_out'], input=pem.encode(), capture_output=True, check=True).stdout.decode()


class S:  # minimal stand-in for Server used by accounts.new_account
    port = PORT


async def pull(c):
    d = await c(functions.updates.GetDifferenceRequest(pts=1, date=0, qts=-1))
    return list(getattr(d, 'new_messages', []))


async def main():
    results = {}
    wait_healthy()
    load_server_key(key_pem())
    sfx = random.randint(1000, 9999)
    a, ra, acta = await new_account(S, f'dka{sfx}')
    b, rb, actb = await new_account(S, f'dkb{sfx}')
    pb = input_peer(actb.authorization.user)
    for i in range(5):
        await a(functions.messages.SendMessageRequest(peer=pb, message=f'docker-{i}', random_id=random.getrandbits(62)))
    await a.disconnect(); await b.disconnect()

    sh('docker', 'restart', NAME)           # SIGTERM -> graceful stop -> start
    results['graceful_restart_healthy_after_s'] = round(wait_healthy(), 1)
    b2 = make_client('127.0.0.1', PORT); await b2.connect()
    await b2(zg.Login(actb.authorization.user.username, code_for(rb.manual_key, 1)))
    msgs = await pull(b2)
    results['after_docker_restart_messages'] = sorted(m.message for m in msgs)
    assert len(msgs) == 5, results
    await b2(zg.AckMessages([m.id for m in msgs[:2]]))
    await b2.disconnect()

    sh('docker', 'kill', NAME)              # SIGKILL
    sh('docker', 'start', NAME)
    results['kill9_restart_healthy_after_s'] = round(wait_healthy(), 1)
    b3 = make_client('127.0.0.1', PORT); await b3.connect()
    await b3(zg.Login(actb.authorization.user.username, code_for(rb.manual_key, 2)))
    left = await pull(b3)
    results['after_kill9_messages'] = sorted(m.message for m in left)
    assert len(left) == 3, results
    r = await a_send_after_kill(a, ra, acta, pb)
    results['post_kill_send_ids_increase'] = r
    await b3.disconnect()

    # file modes inside the volume
    modes = sh('docker', 'exec', NAME, 'sh', '-c', "stat -c '%a %n' /data /data/state /data/default-private.key /data/state/zerogram.db /data/state/zerogram-epoch")
    results['modes'] = modes.splitlines()
    results['user'] = sh('docker', 'exec', NAME, 'id', '-u')
    print(json.dumps(results, indent=2, ensure_ascii=False))
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'results'); os.makedirs(out, exist_ok=True)
    json.dump(results, open(os.path.join(out, 'docker-persistence.json'), 'w'), indent=2, ensure_ascii=False)


async def a_send_after_kill(a_old, ra, acta, pb):
    a = make_client('127.0.0.1', PORT); await a.connect()
    await a(zg.Login(acta.authorization.user.username, code_for(ra.manual_key, 1)))
    r1 = await a(functions.messages.SendMessageRequest(peer=pb, message='after-kill-1', random_id=random.getrandbits(62)))
    r2 = await a(functions.messages.SendMessageRequest(peer=pb, message='after-kill-2', random_id=random.getrandbits(62)))
    await a.disconnect()
    assert r2.id > r1.id and r2.pts > r1.pts
    return {'id1': r1.id, 'id2': r2.id, 'pts1': r1.pts, 'pts2': r2.pts}

asyncio.run(main())
