#!/usr/bin/env python3
"""Staged load test for a running Zerogram server (real MTProto clients via Telethon).

Provisioning (not measured): creates N accounts and stores their sessions.
Measured stage: N concurrent connections; every client sends a short text message to its partner every
`--interval` seconds; receivers acknowledge after "storing" it (zg.ackMessages). Reports connection
success, send/ack errors, send->receive latency and ACK->delivered-mark latency percentiles.
Server CPU/RAM are sampled by sample_resources() (docker stats) when --container is given.

usage: zg_load.py --port 5222 --pem server.pem --levels 100,300,500 --duration 90 --container zerogram-bench
"""
import argparse, asyncio, json, os, random, statistics, subprocess, sys, time, multiprocessing as mp

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'e2e'))
from telethon import TelegramClient, functions, types, events
from telethon.sessions import StringSession, MemorySession
import telethon.network.connection as conn
from zgtest import zg
from zgtest import rsapad  # noqa: F401
from zgtest.helper import load_server_key
import pyotp

API_ID, API_HASH = 12345, 'abcdef'


def make_client(session, port, host='127.0.0.1'):
    c = TelegramClient(session, API_ID, API_HASH, connection=conn.ConnectionTcpObfuscated,
                       device_model='zg-load', app_version='zg-load', flood_sleep_threshold=0,
                       request_retries=2, connection_retries=3, retry_delay=1, auto_reconnect=True)
    if isinstance(session, MemorySession):
        c.session.set_dc(1, host, port)
    return c


# ---------------------------------------------------------------------------- provisioning

async def provision_one(i, port, prefix):
    username = f'{prefix}{i:05d}'
    c = make_client(MemorySession(), port)
    await c.connect()
    reg = await c(zg.Register(username, f'Load {i}', f'{username}@example.com'))
    act = await c(zg.Activate(username, pyotp.TOTP(reg.manual_key).now()))
    u = act.authorization.user
    sess = StringSession.save(c.session)
    await c.disconnect()
    return {'i': i, 'username': username, 'user_id': u.id, 'access_hash': u.access_hash, 'session': sess}


def provision_worker(args):
    start, end, port, prefix, pem = args
    load_server_key(pem)

    async def run():
        sem = asyncio.Semaphore(8)
        out = []

        async def one(i):
            async with sem:
                for attempt in range(4):
                    try:
                        out.append(await provision_one(i, port, prefix))
                        return
                    except Exception as e:  # noqa
                        await asyncio.sleep(1 + attempt)
        await asyncio.gather(*(one(i) for i in range(start, end)))
        return out
    return asyncio.run(run())


def provision(n, port, pem, prefix, cache, procs):
    have = {}
    if os.path.exists(cache):
        for a in json.load(open(cache)):
            have[a['i']] = a
    missing = [i for i in range(n) if i not in have]
    if missing:
        chunks = [(missing[k::procs][0], missing[k::procs][-1] + 1, port, prefix, pem) for k in range(procs) if missing[k::procs]]
        # simple contiguous split of the missing indices
        step = max(1, len(missing) // procs + 1)
        chunks = [(min(missing[j:j + step]), max(missing[j:j + step]) + 1, port, prefix, pem) for j in range(0, len(missing), step)]
        with mp.Pool(len(chunks)) as pool:
            for part in pool.map(provision_worker, chunks):
                for a in part:
                    have[a['i']] = a
        json.dump(sorted(have.values(), key=lambda a: a['i']), open(cache, 'w'))
    return [have[i] for i in range(n) if i in have]


# ---------------------------------------------------------------------------- measured stage

class Stats:
    def __init__(self):
        self.connect_ok = 0
        self.connect_fail = 0
        self.sent = 0
        self.send_err = {}
        self.received = 0
        self.acked = 0
        self.ack_err = 0
        self.delivered_marks = 0
        self.dup_received = 0
        self.lat_deliver = []   # send -> peer receives (s)
        self.lat_ack = []       # ack rpc round trip (s)
        self.lat_mark = []      # send -> sender sees delivered mark (s)
        self.connect_time = []

    def err(self, name):
        self.send_err[name] = self.send_err.get(name, 0) + 1


async def client_loop(acct, partner, port, interval, until, stats, ready):
    c = make_client(StringSession(acct['session']), port)
    t0 = time.time()
    try:
        await c.connect()
        await c(functions.updates.GetStateRequest())   # proves the stored authorization works
        stats.connect_ok += 1
        stats.connect_time.append(time.time() - t0)
    except Exception:
        stats.connect_fail += 1
        ready.set()
        return
    sent_at = {}       # random text id -> send time
    seen = set()
    sender_ids = {}    # server id -> send time (for delivered marks)

    async def on_update(update):
        now = time.time()
        msg = None
        if isinstance(update, types.UpdateNewMessage):
            msg = update.message
        elif isinstance(update, types.UpdateShortMessage):
            msg = update
        elif isinstance(update, types.UpdateReadHistoryOutbox):
            upto = update.max_id
            for sid in [k for k in sender_ids if k <= upto]:
                stats.lat_mark.append(now - sender_ids.pop(sid))
                stats.delivered_marks += 1
            return
        if msg is None or getattr(msg, 'out', False):
            return
        text = getattr(msg, 'message', '') or ''
        if not text.startswith('LT|'):
            return
        mid = msg.id
        if mid in seen:
            stats.dup_received += 1
            return
        seen.add(mid)
        stats.received += 1
        try:
            stats.lat_deliver.append(now - float(text.split('|')[1]))
        except Exception:
            pass
        t1 = time.time()
        try:
            await c(zg.AckMessages([mid]))
            stats.acked += 1
            stats.lat_ack.append(time.time() - t1)
        except Exception:
            stats.ack_err += 1

    c.add_event_handler(on_update, events.Raw)
    ready.set()
    peer = types.InputPeerUser(partner['user_id'], partner['access_hash'])
    await asyncio.sleep(random.random() * interval)
    while time.time() < until:
        t = time.time()
        try:
            r = await c(functions.messages.SendMessageRequest(
                peer=peer, message=f'LT|{t:.6f}|{acct["i"]}', random_id=random.getrandbits(62)))
            stats.sent += 1
            sid = getattr(r, 'id', None)
            if sid:
                sender_ids[sid] = t
        except Exception as e:
            stats.err(type(e).__name__)
        await asyncio.sleep(interval * (0.7 + 0.6 * random.random()))
    await asyncio.sleep(3)   # let in-flight acks/marks land
    try:
        await c.disconnect()
    except Exception:
        pass


def stage_worker(args):
    accts, order, port, interval, until, pem = args
    load_server_key(pem)
    stats = Stats()

    async def run():
        sem_ready = []
        tasks = []
        for idx in order:
            acct = accts[idx]
            partner = accts[idx ^ 1] if (idx ^ 1) < len(accts) else accts[idx - 1]
            ev = asyncio.Event()
            sem_ready.append(ev)
            tasks.append(asyncio.create_task(client_loop(acct, partner, port, interval, until, stats, ev)))
            await asyncio.sleep(0.02)   # ramp connections instead of one thundering herd
        await asyncio.gather(*tasks, return_exceptions=True)
    asyncio.run(run())
    return stats.__dict__


def pct(values, p):
    if not values:
        return None
    values = sorted(values)
    k = min(len(values) - 1, int(round((p / 100) * (len(values) - 1))))
    return round(values[k], 4)


def summarize(parts, n, duration, resources):
    agg = {'sent': 0, 'received': 0, 'acked': 0, 'ack_err': 0, 'delivered_marks': 0, 'dup_received': 0,
           'connect_ok': 0, 'connect_fail': 0}
    errs, d, a, m, ct = {}, [], [], [], []
    for p in parts:
        for k in agg:
            agg[k] += p[k]
        for k, v in p['send_err'].items():
            errs[k] = errs.get(k, 0) + v
        d += p['lat_deliver']; a += p['lat_ack']; m += p['lat_mark']; ct += p['connect_time']
    out = {'connections_requested': n, **agg, 'send_errors': errs, 'duration_s': duration,
           'send_to_receive_s': {'p50': pct(d, 50), 'p95': pct(d, 95), 'p99': pct(d, 99), 'max': pct(d, 100)},
           'ack_rtt_s': {'p50': pct(a, 50), 'p95': pct(a, 95), 'p99': pct(a, 99)},
           'send_to_delivered_mark_s': {'p50': pct(m, 50), 'p95': pct(m, 95), 'p99': pct(m, 99)},
           'connect_s': {'p50': pct(ct, 50), 'p95': pct(ct, 95)},
           'resources': resources}
    sent = max(1, agg['sent'])
    out['loss_or_pending'] = agg['sent'] - agg['received']
    out['error_rate'] = round((sum(errs.values()) + agg['ack_err'] + agg['connect_fail']) / (sent + agg['connect_ok'] + agg['connect_fail']), 5)
    return out


class Sampler:
    """Samples CPU% and RSS of the server container (docker stats) in the background."""

    def __init__(self, container):
        self.container = container
        self.samples = []
        self.proc = None
        self.stop = False

    def start(self):
        if not self.container:
            return
        import threading
        def loop():
            while not self.stop:
                try:
                    out = subprocess.run(['docker', 'stats', '--no-stream', '--format', '{{.CPUPerc}}|{{.MemUsage}}', self.container],
                                         capture_output=True, text=True, timeout=20).stdout.strip()
                    cpu, mem = out.split('|')
                    used = mem.split('/')[0].strip()
                    mult = {'KiB': 1 / 1024, 'MiB': 1, 'GiB': 1024, 'B': 1 / 1048576}
                    num = float(''.join(ch for ch in used if ch in '0123456789.'))
                    unit = ''.join(ch for ch in used if ch.isalpha())
                    self.samples.append((float(cpu.strip('%')), num * mult.get(unit, 1)))
                except Exception:
                    pass
                time.sleep(1)
        self.t = threading.Thread(target=loop, daemon=True)
        self.t.start()

    def finish(self):
        self.stop = True
        if not self.samples:
            return None
        cpus = [c for c, _ in self.samples]
        mems = [m for _, m in self.samples]
        return {'cpu_percent_mean': round(statistics.mean(cpus), 1), 'cpu_percent_max': round(max(cpus), 1),
                'cpu_percent_p95': round(sorted(cpus)[int(0.95 * (len(cpus) - 1))], 1),
                'rss_mib_mean': round(statistics.mean(mems), 1), 'rss_mib_max': round(max(mems), 1), 'samples': len(cpus),
                'note': 'docker stats: 100% = one vCPU'}


def run_level(accts, n, port, interval, duration, pem, procs, container):
    use = accts[:n]
    until = time.time() + duration + 20   # includes ramp-up
    parts_in = [(use, list(range(k, n, procs)), port, interval, until, pem) for k in range(procs)]
    sampler = Sampler(container)
    sampler.start()
    t0 = time.time()
    with mp.Pool(procs) as pool:
        parts = pool.map(stage_worker, parts_in)
    res = sampler.finish()
    return summarize(parts, n, round(time.time() - t0, 1), res)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--port', type=int, default=5222)
    ap.add_argument('--pem', required=True)
    ap.add_argument('--levels', default='100,300,500')
    ap.add_argument('--duration', type=int, default=90)
    ap.add_argument('--interval', type=float, default=10.0)
    ap.add_argument('--procs', type=int, default=4)
    ap.add_argument('--prefix', default='loaduser')
    ap.add_argument('--cache', default='load-accounts.json')
    ap.add_argument('--container', default='')
    ap.add_argument('--report', default='results/load-report.json')
    a = ap.parse_args()
    levels = [int(x) for x in a.levels.split(',')]
    load_server_key(a.pem)
    accts = provision(max(levels), a.port, a.pem, a.prefix, a.cache, a.procs)
    print(f'provisioned {len(accts)} accounts', flush=True)
    report = []
    for n in levels:
        print(f'--- level {n} ---', flush=True)
        r = run_level(accts, n, a.port, a.interval, a.duration, a.pem, a.procs, a.container)
        print(json.dumps(r, indent=1), flush=True)
        report.append(r)
        time.sleep(10)
    os.makedirs(os.path.dirname(a.report), exist_ok=True)
    json.dump(report, open(a.report, 'w'), indent=2)


if __name__ == '__main__':
    main()
