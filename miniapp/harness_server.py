import hashlib
import hmac
import json
import os
import re
import tempfile
import threading
import time
from pathlib import Path

MAX_BODY = 1024
MAX_AGE = 300
NAME = re.compile(r"^[^\\/:\x00-\x1f]{1,24}$")
STATUSES = {"running", "completed", "error", "blocked", "aborted", "interrupted", "idle", "unknown", "disposed", "max-tokens"}


class SnapshotError(ValueError):
    pass


def _number(value, upper=999999999):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and 0 <= value <= upper


def parse_snapshot(body):
    if not isinstance(body, bytes) or len(body) == 0 or len(body) > MAX_BODY:
        raise SnapshotError("body")
    try:
        source = json.loads(body.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        raise SnapshotError("json")
    if not isinstance(source, dict) or set(source) != {"v", "snapshot", "rows", "limits"} or source["v"] != 1 or not isinstance(source["snapshot"], int) or source["snapshot"] < 0:
        raise SnapshotError("root")
    if not isinstance(source["rows"], list) or not isinstance(source["limits"], list) or len(source["rows"]) > 6 or len(source["limits"]) > 4:
        raise SnapshotError("list")
    for row in source["rows"]:
        if not isinstance(row, dict) or set(row) != {"name", "status", "seconds"} or not isinstance(row["name"], str) or not NAME.fullmatch(row["name"]) or "token" in row["name"].lower() or "secret" in row["name"].lower() or not isinstance(row["status"], str) or row["status"] not in STATUSES or not isinstance(row["seconds"], int) or not _number(row["seconds"]):
            raise SnapshotError("row")
    for limit in source["limits"]:
        if not isinstance(limit, dict) or not set(limit).issubset({"label", "remaining", "total", "reset", "amount", "currency"}) or "label" not in limit or not isinstance(limit["label"], str) or not NAME.fullmatch(limit["label"]):
            raise SnapshotError("limit")
        if any(not _number(limit[key]) for key in ("remaining", "total", "amount") if key in limit) or ("reset" in limit and (not isinstance(limit["reset"], int) or isinstance(limit["reset"], bool) or limit["reset"] < 0 or limit["reset"] > 4102444800)) or ("currency" in limit and (not isinstance(limit["currency"], str) or len(limit["currency"]) > 8)):
            raise SnapshotError("limit-value")
    return source


def verify_snapshot(body, signature, timestamp, owner, token, now=None):
    if not isinstance(owner, str) or not owner.isdigit() or not isinstance(signature, str) or not re.fullmatch(r"[0-9a-f]{64}", signature):
        raise SnapshotError("headers")
    try: stamp = int(timestamp)
    except (TypeError, ValueError): raise SnapshotError("timestamp")
    now = int(time.time()) if now is None else int(now)
    if abs(now - stamp) > MAX_AGE: raise SnapshotError("expired")
    key = hmac.new(token.encode("utf-8"), b"harness-key-v1", hashlib.sha256).digest()
    signed = "harness-v1|%s|%s|%s" % (owner, stamp, hashlib.sha256(body).hexdigest())
    expected = hmac.new(key, signed.encode("utf-8"), hashlib.sha256).hexdigest()
    if not hmac.compare_digest(expected, signature): raise SnapshotError("signature")
    return True


class HarnessStore:
    _write_lock = threading.Lock()
    def __init__(self, directory, owner):
        self.directory, self.owner = Path(directory), str(owner)
        self.path = self.directory / "harness-state.json"

    def read(self):
        try:
            value = json.loads(self.path.read_text(encoding="utf-8"))
            return value.get("snapshot") if isinstance(value, dict) and isinstance(value.get("published"), int) and isinstance(value.get("snapshot"), dict) else None
        except (OSError, json.JSONDecodeError): return None

    def accept(self, body, signature, timestamp, token, now=None):
        if not self.owner.isdigit(): raise SnapshotError("owner")
        verify_snapshot(body, signature, timestamp, self.owner, token, now)
        value, stamp = parse_snapshot(body), int(timestamp)
        self.directory.mkdir(mode=0o700, parents=True, exist_ok=True)
        with self._write_lock:
            try:
                envelope = json.loads(self.path.read_text(encoding="utf-8")); previous = int(envelope.get("published", -1))
            except (OSError, ValueError, json.JSONDecodeError, AttributeError): previous = -1
            if stamp <= previous: raise SnapshotError("replay")
            fd, name = tempfile.mkstemp(prefix=".harness-", dir=str(self.directory))
            try:
                if hasattr(os, "fchmod"): os.fchmod(fd, 0o600)
                with os.fdopen(fd, "w", encoding="utf-8") as output:
                    json.dump({"published": stamp, "snapshot": value}, output, ensure_ascii=False, separators=(",", ":")); output.flush(); os.fsync(output.fileno())
                os.replace(name, self.path)
            finally:
                if os.path.exists(name): os.unlink(name)
        return value
