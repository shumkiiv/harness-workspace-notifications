import hashlib
import hmac
import json
import time
import urllib.parse


class InitDataError(ValueError): pass


def verify_init_data(init_data, token, owner, max_age=3600, now=None):
    if not isinstance(init_data, str) or len(init_data) > 8192: raise InitDataError("init-data")
    try: pairs = urllib.parse.parse_qsl(init_data, strict_parsing=True, keep_blank_values=True)
    except ValueError: raise InitDataError("parse")
    values = dict(pairs)
    if len(values) != len(pairs) or not {"auth_date", "hash", "user"}.issubset(values): raise InitDataError("fields")
    try: auth_date, user = int(values["auth_date"]), json.loads(values["user"])
    except (ValueError, json.JSONDecodeError): raise InitDataError("value")
    if not isinstance(user, dict) or str(user.get("id", "")) != str(owner): raise InitDataError("owner")
    if abs((int(time.time()) if now is None else int(now)) - auth_date) > max_age: raise InitDataError("expired")
    data_check = "\n".join("%s=%s" % (key, values[key]) for key in sorted(values) if key != "hash")
    secret = hmac.new(b"WebAppData", token.encode("utf-8"), hashlib.sha256).digest()
    if not hmac.compare_digest(hmac.new(secret, data_check.encode("utf-8"), hashlib.sha256).hexdigest(), values["hash"]): raise InitDataError("hash")
    return str(owner)
