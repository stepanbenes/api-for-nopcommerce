#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."
export NOP_ROOT="$(cd "${NOP_ROOT:-../nopCommerce}" && pwd)"
export NOP_PORT="${NOP_PORT:-5087}"
export NOP_DB_PASSWORD="$(openssl rand -hex 24)"
export NOP_API_SIGNING_KEY="$(openssl rand -hex 32)"
export NOP_OPENAPI_ADMIN_PASSWORD="$(openssl rand -hex 24)"

project="nop-openapi-$$"
compose=(docker compose --project-name "$project")
cleanup() {
    status=$?
    if [[ "$status" != 0 ]]; then
        "${compose[@]}" ps >&2 || true
    fi
    "${compose[@]}" down --volumes >&2 || true
    exit "$status"
}
trap cleanup EXIT

"${compose[@]}" up --build -d

python3 - <<'PY'
import http.cookiejar
import os
from html.parser import HTMLParser
import time
import urllib.parse
import urllib.request


class InstallForm(HTMLParser):
    token = None

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "input" and attrs.get("name") == "__RequestVerificationToken":
            self.token = attrs.get("value")


base = f"http://127.0.0.1:{os.environ['NOP_PORT']}"
opener = urllib.request.build_opener(
    urllib.request.ProxyHandler({}),
    urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()),
)
for attempt in range(60):
    try:
        with opener.open(base + "/install", timeout=5) as response:
            form = InstallForm()
            form.feed(response.read().decode())
        if form.token:
            break
    except OSError:
        pass
    time.sleep(2)
else:
    raise SystemExit("The isolated nopCommerce installer did not become ready.")

password = os.environ["NOP_OPENAPI_ADMIN_PASSWORD"]
payload = urllib.parse.urlencode({
    "__RequestVerificationToken": form.token,
    "AdminEmail": "openapi-ci@example.test",
    "AdminPassword": password,
    "ConfirmPassword": password,
    "DataProvider": "3",
    "ConnectionStringRaw": "true",
    "ConnectionString": (
        "Host=db;Database=nopcommerce;Username=nopcommerce;"
        f"Password={os.environ['NOP_DB_PASSWORD']}"
    ),
    "InstallSampleData": "false",
    "SubscribeNewsletters": "false",
}).encode()
with opener.open(base + "/install", payload, timeout=240) as response:
    if "window.location.replace(" not in response.read().decode():
        raise SystemExit("nopCommerce did not complete installation.")
PY

"${compose[@]}" restart web

python3 - <<'PY'
import difflib
from itertools import islice
import json
import os
from pathlib import Path
import sys
import time
import urllib.request

url = f"http://127.0.0.1:{os.environ['NOP_PORT']}/api/swagger/v1/swagger.json"
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
for attempt in range(60):
    try:
        with opener.open(url, timeout=5) as response:
            live = json.load(response)
        if isinstance(live, dict) and "openapi" in live and "paths" in live:
            break
    except (OSError, ValueError):
        pass
    time.sleep(2)
else:
    raise SystemExit("The installed API plugin did not serve its OpenAPI document.")

snapshot = Path("ClientApp/nopapi.swagger.json")
committed = json.loads(snapshot.read_text(encoding="utf-8-sig"))
expected = json.dumps(committed, indent=2, sort_keys=True).splitlines()
actual = json.dumps(live, indent=2, sort_keys=True).splitlines()
if expected != actual:
    diff = difflib.unified_diff(expected, actual, fromfile=str(snapshot), tofile="running API")
    print("\n".join(islice(diff, 200)), file=sys.stderr)
    raise SystemExit("The checked-in OpenAPI is stale. Refresh it from the running plugin.")
print(f"OpenAPI matches the running plugin ({len(live['paths'])} paths).")
PY
