"""Starts, stops and kills a real Zerogram (Ferrite) server process for end-to-end tests."""
import os, re, signal, socket, subprocess, time, shutil, pathlib, tempfile

REPO = pathlib.Path(__file__).resolve().parents[3]
DLL = os.environ.get('ZG_SERVER_DLL', str(REPO / 'Ferrite/bin/Release/net10.0/Ferrite.dll'))
DOTNET = os.environ.get('DOTNET', shutil.which('dotnet') or 'dotnet')
WORK = pathlib.Path(os.environ.get('ZG_WORK', tempfile.gettempdir())) / 'zerogram-e2e'


def free_port():
    with socket.socket() as s:
        s.bind(('127.0.0.1', 0))
        return s.getsockname()[1]


class Server:
    def __init__(self, name, env=None, port=None, fresh=True):
        self.name = name
        self.dir = WORK / name
        if fresh and self.dir.exists():
            shutil.rmtree(self.dir)
        self.dir.mkdir(parents=True, exist_ok=True)
        self.port = port or free_port()
        self.env = dict(env or {})
        self.proc = None
        self.log_path = self.dir / 'server.log'

    @property
    def data(self):
        return self.dir / 'data'

    def start(self, timeout=90):
        env = dict(os.environ)
        env.update({
            'FERRITE_PUBLIC_ADDRESS': '127.0.0.1',
            'FERRITE_PORT': str(self.port),
            'FERRITE_DATA_PATH': str(self.data),
            'DOTNET_CLI_TELEMETRY_OPTOUT': '1',
        })
        env.update(self.env)
        log = open(self.log_path, 'ab')
        self._log_offset = self.log_path.stat().st_size if self.log_path.exists() else 0
        self.proc = subprocess.Popen([DOTNET, DLL], cwd=self.dir, env=env, stdout=log, stderr=subprocess.STDOUT)
        deadline = time.time() + timeout
        while time.time() < deadline:
            if self.proc.poll() is not None:
                raise RuntimeError(f'server exited early: {self.tail()}')
            if b'Server is listening' in self.log_path.read_bytes()[self._log_offset:]:
                return self
            time.sleep(0.3)
        raise TimeoutError(f'server did not start: {self.tail()}')

    def tail(self, n=2000):
        return self.log_path.read_bytes()[-n:].decode('utf8', 'replace')

    def stop(self, timeout=60):
        """Graceful stop (SIGTERM), as `docker stop` does."""
        if self.proc and self.proc.poll() is None:
            self.proc.send_signal(signal.SIGTERM)
            self.proc.wait(timeout)
        self.proc = None

    def kill9(self):
        if self.proc and self.proc.poll() is None:
            self.proc.kill()
            self.proc.wait()
        self.proc = None

    def restart(self, graceful=True):
        (self.stop if graceful else self.kill9)()
        return self.start()

    def public_key_pkcs1(self):
        """RSA public key of the running server in PKCS#1 PEM (what Telethon expects)."""
        text = self.log_path.read_text(errors='replace')
        m = re.search(r'-----BEGIN PUBLIC KEY-----.*?-----END PUBLIC KEY-----', text, re.S)
        pem = m.group(0)
        out = subprocess.run(['openssl', 'rsa', '-pubin', '-RSAPublicKey_out'], input=pem.encode(),
                             capture_output=True, check=True).stdout.decode()
        return out
