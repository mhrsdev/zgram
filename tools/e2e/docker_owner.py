#!/usr/bin/env python3
"""Checks owner sign-in against the real Docker image: owner-init inside a read-only container, then the server
(started on the same volume) accepts the printed 12 words and serves its public key on the bootstrap port.
Usage: docker_owner.py   (needs docker, image zerogram:local; uses --network host, ports 5222/5223)"""
import asyncio, base64, json, os, re, subprocess, sys, time, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from zgtest import zg, owner as zo
from zgtest.helper import make_client, load_server_key

VOL, NAME, IMAGE = 'zg-owner-test', 'zg-owner-test', os.environ.get('ZG_IMAGE', 'zerogram:local')
COMMON = ['--network', 'host', '--read-only', '--tmpfs', '/tmp:size=64m,mode=1777', '--cap-drop', 'ALL',
          '--security-opt', 'no-new-privileges', '-v', f'{VOL}:/data', '-e', 'FERRITE_PUBLIC_ADDRESS=127.0.0.1',
          '-e', 'ZEROGRAM_MASTER_KEY=' + base64.b64encode(os.urandom(32)).decode()]


def sh(*a, **k):
    return subprocess.run(a, capture_output=True, text=True, **k)


async def main():
    sh('docker', 'rm', '-f', NAME); sh('docker', 'volume', 'rm', VOL)
    init = sh('docker', 'run', '--rm', *COMMON, IMAGE, '--owner-init')
    assert init.returncode == 0, init.stderr
    words = [l.strip() for l in init.stdout.splitlines() if len(l.split()) == 12][0]
    assert zo.normalize_mnemonic(words) == words
    again = sh('docker', 'run', '--rm', *COMMON, IMAGE, '--owner-init')
    assert again.returncode == 2, again.returncode
    sh('docker', 'run', '-d', '--name', NAME, *COMMON, IMAGE)
    try:
        for _ in range(90):
            logs = sh('docker', 'logs', NAME).stdout + sh('docker', 'logs', NAME).stderr
            if 'Server is listening' in logs:
                break
            time.sleep(1)
        pem = re.search(r'-----BEGIN PUBLIC KEY-----.*?-----END PUBLIC KEY-----', logs, re.S).group(0)
        pkcs1 = subprocess.run(['openssl', 'rsa', '-pubin', '-RSAPublicKey_out'], input=pem.encode(), capture_output=True, check=True).stdout
        load_server_key(pkcs1.decode())
        der = base64.b64decode(''.join(l for l in pkcs1.decode().splitlines() if not l.startswith('-----')))
        fp = zo.fingerprint_of_pkcs1(der)
        boot = json.loads(urllib.request.urlopen('http://127.0.0.1:5223/zg/key', timeout=10).read())
        assert base64.b64decode(boot['key']) == der and int(boot['fp'], 16) == fp & (2 ** 64 - 1)
        c = make_client('127.0.0.1', 5222); await c.connect()
        auth = await zo.owner_login(c, words, fp)
        assert auth.user.username == 'owner'
        info = await c(zg.GetOwnerInfo())
        msg = zo.open_connect_message(info.connect_message)
        assert msg['h'] == '127.0.0.1' and msg['p'] == 5222 and base64.b64decode(msg['k']) == der
        await c.disconnect()
        print(json.dumps({'owner_init_in_container': 'ok', 'overwrite_refused_rc': again.returncode,
                          'bootstrap_key_matches': True, 'owner_login_with_printed_words': 'ok',
                          'connect_message_decrypts': True, 'words_in_logs': words in logs}, indent=2))
        assert words not in logs
    finally:
        sh('docker', 'rm', '-f', NAME); sh('docker', 'volume', 'rm', VOL)

asyncio.run(main())
