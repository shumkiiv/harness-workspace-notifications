"""Adapter factory for the existing legacy ThreadingHTTPServer handler.

The deployer imports this module from a systemd user drop-in.  It must pass
the legacy module's existing `settings()` result; this module never reads a
token or owner ID from a new source.
"""
import json
import time
import mimetypes
import argparse
import importlib.util
import os
import sys
from http import HTTPStatus
from http.server import ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlsplit

from .auth import InitDataError, verify_init_data
from .harness_server import HarnessStore, SnapshotError

STATIC = {"/harness/": "index.html", "/harness/index.html": "index.html", "/harness/app.css": "app.css", "/harness/app.js": "app.js"}


def handler(legacy_handler, settings, state_dir, static_dir=None):
    static_dir = Path(static_dir or Path(__file__).with_name("static"))
    class HarnessAdapter(legacy_handler):
        def _harness_settings(self):
            value = settings()
            token, owner = value if isinstance(value, tuple) and len(value) == 2 else (value["token"], value["owner"])
            return token, owner

        def _route(self):
            parsed = urlsplit(self.path)
            return None if parsed.query or parsed.fragment else parsed.path
        def _header(self, name):
            values = self.headers.get_all(name)
            if values is None or len(values) != 1: raise SnapshotError("header")
            return values[0]
        def _init_owner(self):
            token, owner = self._harness_settings()
            return verify_init_data(self._header("X-Telegram-Init-Data"), token, owner)
        def _reject(self, status): self.send_response(status); self.send_header("Content-Length", "0"); self.end_headers()
        def _json(self, value):
            raw = json.dumps(value, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
            self.send_response(HTTPStatus.OK); self.send_header("Content-Type", "application/json; charset=utf-8"); self.send_header("Cache-Control", "no-store"); self.send_header("Content-Length", str(len(raw))); self.end_headers()
            if self.command != "HEAD": self.wfile.write(raw)
        def _static(self):
            filename = STATIC.get(self._route())
            if not filename: return False
            try: raw = (static_dir / filename).read_bytes()
            except OSError: self._reject(HTTPStatus.NOT_FOUND); return True
            self.send_response(HTTPStatus.OK); self.send_header("Content-Type", mimetypes.guess_type(filename)[0] or "application/octet-stream"); self.send_header("Content-Length", str(len(raw))); self.send_header("Cache-Control", "no-store"); self.end_headers()
            if self.command != "HEAD": self.wfile.write(raw)
            return True
        def do_GET(self):
            route = self._route()
            if route in STATIC: self._static(); return
            if route == "/harness/api/state":
                try:
                    owner = self._init_owner(); store = HarnessStore(state_dir, owner); value = store.read()
                    self._json({"snapshot": value, "stale": value is None or int(time.time()) - int(value.get("snapshot", 0)) > 90}); return
                except (InitDataError, SnapshotError): self._reject(HTTPStatus.UNAUTHORIZED); return
            return super(HarnessAdapter, self).do_GET()
        def do_HEAD(self):
            if self._route() in STATIC: self._static(); return
            return super(HarnessAdapter, self).do_HEAD()
        def do_POST(self):
            if self._route() != "/harness/api/snapshot": return super(HarnessAdapter, self).do_POST()
            try:
                token, owner = self._harness_settings()
                if self._header("X-Harness-Owner") != owner: raise SnapshotError("owner")
                length = int(self._header("Content-Length"))
                if length < 0 or length > 1024: raise SnapshotError("length")
                previous_timeout = self.connection.gettimeout(); self.connection.settimeout(5)
                try: body = self.rfile.read(length)
                finally: self.connection.settimeout(previous_timeout)
                HarnessStore(state_dir, owner).accept(body, self._header("X-Harness-Signature"), self._header("X-Harness-Timestamp"), token)
                self._reject(HTTPStatus.NO_CONTENT)
            except (SnapshotError, ValueError, OSError): self._reject(HTTPStatus.FORBIDDEN)
    return HarnessAdapter


def main(argv=None):
    parser = argparse.ArgumentParser(description="Harness Mini App legacy-handler adapter")
    parser.add_argument("--legacy-server", required=True); parser.add_argument("--state-dir", required=True)
    parser.add_argument("--bind", default=os.environ.get("AGENT_TASK_MINI_APP_BIND", "127.0.0.1")); parser.add_argument("--port", type=int, default=int(os.environ.get("AGENT_TASK_MINI_APP_PORT", "8093")))
    args = parser.parse_args(argv); source = Path(args.legacy_server).resolve(); sys.path.insert(0, str(source.parent))
    spec = importlib.util.spec_from_file_location("harness_legacy_server", str(source))
    if spec is None or spec.loader is None: raise SystemExit("legacy server cannot be imported")
    legacy = importlib.util.module_from_spec(spec); sys.modules[spec.name] = legacy; spec.loader.exec_module(legacy)
    if not callable(getattr(legacy, "settings", None)) or not isinstance(getattr(legacy, "Handler", None), type): raise SystemExit("legacy server needs Handler and settings")
    ThreadingHTTPServer((args.bind, args.port), handler(legacy.Handler, legacy.settings, args.state_dir)).serve_forever()


if __name__ == "__main__": main()
