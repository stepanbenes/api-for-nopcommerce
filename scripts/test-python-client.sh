#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
scratch="$(mktemp -d)"
trap 'rm -rf -- "$scratch"' EXIT

"${NOP_PYTHON:-python3}" -m venv "$scratch/venv"
export PATH="$scratch/venv/bin:$PATH"
python -m pip --disable-pip-version-check install --quiet --no-cache-dir 'openapi-python-client==0.29.1'

spec="$root/ClientApp/nopapi.swagger.json"
openapi-python-client generate --path "$spec" --output-path "$scratch/nop_api_client" --meta none --fail-on-warning
python -m compileall -q "$scratch/nop_api_client"
PYTHONOPTIMIZE=0 PYTHONPATH="$scratch" python "$root/scripts/test-python-client.py" "$spec"
