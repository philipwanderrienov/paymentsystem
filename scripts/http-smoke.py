"""HTTP integration checks against a temporary, private demo instance."""
from pathlib import Path
import json
import socket
import subprocess
import tempfile
import time
import urllib.request
import urllib.error

root = Path(__file__).resolve().parents[1]
with socket.socket() as sock:
    sock.bind(('127.0.0.1', 0))
    port = sock.getsockname()[1]
base = f'http://127.0.0.1:{port}'
checks = 0

def request(path, body=None, expected=200):
    global checks
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(base + path, data=data, headers={'Content-Type':'application/json'})
    try:
        response = urllib.request.urlopen(req, timeout=3)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        assert response.code == expected, (path, response.code, expected, response.read())
        checks += 1
        raw = response.read()
        return json.loads(raw) if raw else None

with tempfile.TemporaryDirectory(prefix='victoria-http-') as directory:
    with tempfile.TemporaryFile() as log:
        process = subprocess.Popen([
            'dotnet', str(root / 'src/PaymentHub.Simple.Gateway/bin/Debug/net10.0/PaymentHub.Simple.Gateway.dll'),
            '--urls', base, '--Demo:DataDirectory', directory, '--Demo:WorkerEnabled', 'false'
        ], cwd=root, stdout=log, stderr=log)
        try:
            deadline = time.monotonic() + 45
            while True:
                try:
                    request('/health')
                    break
                except (OSError, AssertionError):
                    if process.poll() is not None or time.monotonic() >= deadline:
                        log.seek(0)
                        raise RuntimeError(log.read().decode(errors='replace'))
                    time.sleep(0.2)
            message = (root / 'samples/MT103-VA.txt').read_text()
            body = {'content': message, 'scenario': 'OffUsTimeout'}
            created = request('/api/rtgs/payments', body, 201)
            assert created['status'] == 'Unposting'
            request('/api/rtgs/payments', body, 409)
            request('/api/rtgs/payments', {'content':'invalid'}, 400)
            request('/api/rtgs/payments', {'content':message, 'scenario':99}, 400)
            request('/api/rtgs/payments/MISSING', expected=404)
            request('/api/system/settings')
            for _ in range(4):
                request('/api/rtgs/process', {})
            payment = request('/api/rtgs/payments/DEMO-VA-001')
            assert payment['status'] == 'Settle', payment
            assert payment['paidAmount'] == 997500
            assert any('InquiryStatus' in entry['message'] for entry in payment['audit'])
            print(f'PASS: {checks} HTTP checks (health, import, validation, duplicate, detail, process, inquiry fallback).')
        finally:
            process.terminate()
            try:
                process.wait(timeout=15)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
