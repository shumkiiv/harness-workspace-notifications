import hashlib
import hmac
import json
import tempfile
import time
import urllib.parse
import unittest
from pathlib import Path

from miniapp.harness_server import HarnessStore, SnapshotError, parse_snapshot, verify_snapshot
from miniapp.auth import InitDataError, verify_init_data


TOKEN = "999999:abcdefghijklmnopqrstuvwxyz_123456"
OWNER = "42"


def body(snapshot=100):
    return json.dumps({"v": 1, "snapshot": snapshot, "rows": [{"name": "Demo", "status": "running", "seconds": 10}], "limits": []}, separators=(",", ":")).encode()


def signature(data, stamp):
    key = hmac.new(TOKEN.encode(), b"harness-key-v1", hashlib.sha256).digest()
    message = "harness-v1|%s|%s|%s" % (OWNER, stamp, hashlib.sha256(data).hexdigest())
    return hmac.new(key, message.encode(), hashlib.sha256).hexdigest()


def init_data(auth_date, user_id=OWNER):
    values = {"auth_date": str(auth_date), "query_id": "synthetic-query", "chat_instance": "synthetic-chat", "user": json.dumps({"id": int(user_id)}, separators=(",", ":"))}
    check = "\n".join("%s=%s" % (key, values[key]) for key in sorted(values))
    secret = hmac.new(b"WebAppData", TOKEN.encode(), hashlib.sha256).digest()
    values["hash"] = hmac.new(secret, check.encode(), hashlib.sha256).hexdigest()
    return "&".join("%s=%s" % (key, urllib.parse.quote(values[key], safe="")) for key in values)


class HarnessTests(unittest.TestCase):
    def test_parses_exact_safe_projection(self):
        self.assertEqual(parse_snapshot(body())["rows"][0]["name"], "Demo")
        large_reset = b'{"v":1,"snapshot":1,"rows":[],"limits":[{"label":"Codex","remaining":50,"total":100,"reset":1795000000}]}'
        self.assertEqual(parse_snapshot(large_reset)["limits"][0]["reset"], 1795000000)

    def test_rejects_unknown_schema_and_unsafe_name(self):
        with self.assertRaises(SnapshotError): parse_snapshot(b'{"v":1,"snapshot":1,"rows":[],"limits":[],"leak":1}')
        with self.assertRaises(SnapshotError): parse_snapshot(b'{"v":1,"snapshot":1,"rows":[{"name":"C:\\\\private","status":"running","seconds":1}],"limits":[]}')

    def test_accepts_fresh_signature_and_rejects_bad_or_stale(self):
        stamp = int(time.time()); data = body(stamp)
        self.assertTrue(verify_snapshot(data, signature(data, stamp), str(stamp), OWNER, TOKEN, stamp))
        with self.assertRaises(SnapshotError): verify_snapshot(data, "0" * 64, str(stamp), OWNER, TOKEN, stamp)
        with self.assertRaises(SnapshotError): verify_snapshot(data, signature(data, stamp), str(stamp - 301), OWNER, TOKEN, stamp)

    def test_store_rejects_replay_and_preserves_previous_state(self):
        stamp = int(time.time()); data = body(stamp)
        with tempfile.TemporaryDirectory() as directory:
            store = HarnessStore(Path(directory), OWNER)
            store.accept(data, signature(data, stamp), str(stamp), TOKEN, stamp)
            self.assertEqual(store.read()["snapshot"], stamp)
            with self.assertRaises(SnapshotError): store.accept(data, signature(data, stamp), str(stamp), TOKEN, stamp)
            self.assertEqual(store.read()["snapshot"], stamp)

    def test_init_data_accepts_old_owner_read_and_rejects_boundaries(self):
        self.assertEqual(verify_init_data(init_data(0), TOKEN, OWNER, now=301), OWNER)
        self.assertEqual(verify_init_data(init_data(0), TOKEN, OWNER, now=3599), OWNER)
        with self.assertRaises(InitDataError): verify_init_data(init_data(0), TOKEN, OWNER, now=3601)
        with self.assertRaises(InitDataError):
            verify_init_data(init_data(1, "43"), TOKEN, OWNER, now=1)
