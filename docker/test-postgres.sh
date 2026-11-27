#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

: "${NOP_API_TEST_URL:?Set NOP_API_TEST_URL to the loopback API URL.}"
: "${NOP_API_TEST_USER:?Set NOP_API_TEST_USER to the test API account.}"
: "${NOP_API_TEST_PASSWORD:?Set NOP_API_TEST_PASSWORD to the test API password.}"

container="$(docker compose ps --status running --quiet db)"
if [[ -z "$container" ]]; then
    echo "Start the PostgreSQL service in this Compose project before running the tests." >&2
    exit 1
fi
export NOP_API_TEST_POSTGRES_CONTAINER="$container"
command -v python3 >/dev/null || { echo "Install Python 3 to verify the test results." >&2; exit 1; }

nop_root="$(cd "${NOP_ROOT:-../nopCommerce}" && pwd)"
results="$(mktemp -d)"
trap 'rm -r "$results"' EXIT

dotnet test Nop.Plugin.Api.Tests -c Release -p:NopRoot="$nop_root" \
    --filter 'TestCategory=DatabaseIntegration' \
    --results-directory "$results" --logger 'trx;LogFileName=database-integration.trx'

dotnet test Nop.Plugin.Api.Tests -c Release --no-build -p:NopRoot="$nop_root" \
    --filter 'TestCategory=ExistingReceiptSchema' \
    --results-directory "$results" --logger 'trx;LogFileName=existing-receipts.trx'

python3 - "$results/database-integration.trx" "$results/existing-receipts.trx" <<'PY'
import sys
import xml.etree.ElementTree as xml

def counts(path):
    counters = xml.parse(path).find("./{*}ResultSummary/{*}Counters")
    return tuple(int(counters.get(key, "0")) for key in ("total", "executed", "passed"))

if counts(sys.argv[1]) != (9, 9, 9):
    sys.exit("Expected all nine PostgreSQL integration cases to run and pass.")

if counts(sys.argv[2]) not in ((4, 0, 0), (4, 4, 4)):
    sys.exit("Expected all four legacy cases to pass or to be skipped by the capability gate.")
PY
