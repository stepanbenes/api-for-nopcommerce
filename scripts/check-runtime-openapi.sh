#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."
repository="$PWD"
case "${1:-}" in
    "") export NOP_OPENAPI_RUN_TESTS=false ;;
    --test-api) export NOP_OPENAPI_RUN_TESTS=true ;;
    *) echo "Usage: $0 [--test-api]" >&2; exit 2 ;;
esac
export NOP_ROOT="$(cd "${NOP_ROOT:-../nopCommerce}" && pwd)"
export NOP_PORT="${NOP_PORT:-5087}"
export NOP_DB_PASSWORD="$(openssl rand -hex 24)"
export NOP_API_SIGNING_KEY="$(openssl rand -hex 32)"
export NOP_OPENAPI_ADMIN_PASSWORD="$(openssl rand -hex 24)"

project="nop-openapi-$$"
scratch="$(mktemp -d)"
compose=(docker compose --project-name "$project" -f "$repository/compose.yaml")
cleanup() {
    status=$?
    if [[ "$status" != 0 ]]; then
        "${compose[@]}" ps >&2 || true
    fi
    "${compose[@]}" down --volumes >&2 || true
    rm -rf -- "$scratch"
    exit "$status"
}
trap cleanup EXIT

if [[ -n "${NOP_PLUGIN_PACKAGE_DIR:-}" ]]; then
    NOP_PLUGIN_BUILD_VERSION="$(python3 -c 'import json; print(json.load(open("Nop.Plugin.Api/plugin.json", encoding="utf-8-sig"))["Version"])')"
    export NOP_PLUGIN_BUILD_VERSION
    "${compose[@]}" build web
    "${compose[@]}" run --no-deps --rm -T --entrypoint tar web -C /app/Plugins -cf - Nop.Plugin.Api \
        | tar -xf - -C "$scratch"
    python3 scripts/package-plugin.py "$scratch/Nop.Plugin.Api" "$NOP_PLUGIN_PACKAGE_DIR"
    python3 -m zipfile -e "$NOP_PLUGIN_PACKAGE_DIR/Nop.Plugin.Api.zip" "$scratch/package"

    # Boot nopCommerce with the exact files that will be published. The loader
    # removes the plugin's deps.json from the extracted directory at startup.
    export NOP_PACKAGED_PLUGIN="$scratch/package/Nop.Plugin.Api"
    cat > "$scratch/compose.package.yaml" <<'YAML'
services:
  web:
    volumes:
      - type: bind
        source: ${NOP_PACKAGED_PLUGIN}
        target: /app/Plugins/Nop.Plugin.Api
YAML
    compose+=(-f "$scratch/compose.package.yaml")
    "${compose[@]}" up --no-build -d
else
    "${compose[@]}" up --build -d
fi

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
    "InstallSampleData": os.environ["NOP_OPENAPI_RUN_TESTS"],
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


if [[ "$NOP_OPENAPI_RUN_TESTS" == true ]]; then
    # Only the temporary administrator in this disposable Compose database receives the API role.
    role_count="$("${compose[@]}" exec -T db psql -XqAtw -U nopcommerce -d nopcommerce -v ON_ERROR_STOP=1 <<'SQL'
WITH added_role AS (
    INSERT INTO "Customer_CustomerRole_Mapping" ("Customer_Id", "CustomerRole_Id")
    SELECT customer."Id", role."Id"
    FROM "Customer" AS customer CROSS JOIN "CustomerRole" AS role
    WHERE customer."Email" = 'openapi-ci@example.test' AND role."SystemName" = 'ApiUserRole'
    RETURNING 1
)
SELECT count(*) FROM added_role;
SQL
)"
    if [[ "$role_count" != 1 ]]; then
        echo "Could not assign the API role to exactly one temporary administrator." >&2
        exit 1
    fi
    export NOP_API_TEST_URL="http://127.0.0.1:$NOP_PORT"
    export NOP_API_TEST_USER="openapi-ci@example.test"
    export NOP_API_TEST_PASSWORD="$NOP_OPENAPI_ADMIN_PASSWORD"
    export NOP_API_TEST_POSTGRES_CONTAINER="$("${compose[@]}" ps --status running --quiet db)"
    : "${NOP_API_TEST_POSTGRES_CONTAINER:?The disposable database must be running.}"

    # Resolve the SDK using the pinned upstream checkout's global.json.
    (
        cd "$NOP_ROOT"
        dotnet test "$repository/api-for-nopcommerce.sln" -c Release -p:NopRoot="$NOP_ROOT" \
            --results-directory "$scratch" --logger 'trx;LogFileName=api-release.trx'
    )

    python3 - "$scratch/api-release.trx" <<'PYTEST'
import sys
import xml.etree.ElementTree as xml

report = xml.parse(sys.argv[1])
counters = report.find("./{*}ResultSummary/{*}Counters")
total = int(counters.get("total", "0")) if counters is not None else 0
if total < 572 or any(int(counters.get(key, "0")) != total for key in ("executed", "passed")):
    raise SystemExit("Expected at least 572 API tests with every case executed and passed.")

expected_database_tests = {
    "CheckoutAcceptsAnItemWithoutAttributesAndPartialUpdatePreservesShipping(True)",
    "ProductAndCartItemCanBeCreatedUpdatedAndDeleted(True)",
    "TaxCategoryCanBeListedReadUpdatedAndDeleted(True)",
}
passed = {item.get("testName") for item in report.findall("./{*}Results/{*}UnitTestResult")
          if item.get("outcome") == "Passed"}
missing = expected_database_tests - passed
if missing:
    raise SystemExit(f"Expected all three PostgreSQL cases to run and pass. Missing: {sorted(missing)}")
print(f"Passed {total} Release API tests, including all three PostgreSQL cases.")
PYTEST
fi
