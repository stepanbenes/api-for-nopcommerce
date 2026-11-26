#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

: "${NOP_API_TEST_URL:?Set NOP_API_TEST_URL to the loopback API URL.}"
: "${NOP_API_TEST_USER:?Set NOP_API_TEST_USER to the test API account.}"
: "${NOP_API_TEST_PASSWORD:?Set NOP_API_TEST_PASSWORD to the test API password.}"
if [[ "${NOP_API_TEST_POSTGRES_ALLOW_FAULT_INJECTION:-}" != 1 ]]; then
    echo "Set NOP_API_TEST_POSTGRES_ALLOW_FAULT_INJECTION=1 for a disposable local test store." >&2
    exit 1
fi

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

python3 - "$results/database-integration.trx" <<'PY'
import sys
import xml.etree.ElementTree as xml

counters = xml.parse(sys.argv[1]).find("./{*}ResultSummary/{*}Counters")
expected = {"total": "9", "executed": "9", "passed": "9"}
if counters is None or any(counters.get(name) != count for name, count in expected.items()):
    sys.exit("Expected all nine PostgreSQL integration cases to run and pass.")
PY
