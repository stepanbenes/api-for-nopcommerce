#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export NOP_ROOT="$(cd "${NOP_ROOT:-../nopCommerce}" && pwd)"
export NOP_PORT="${NOP_PORT:-5089}"
export NOP_DB_PASSWORD="Aa9!$(openssl rand -hex 24)"
export NOP_OPENAPI_ADMIN_PASSWORD="$(openssl rand -hex 24)"
export NOP_API_SIGNING_KEY="$(openssl rand -hex 32)"
export NOP_INSTALL_PROVIDER=sqlserver NOP_OPENAPI_RUN_TESTS=true
export NOP_API_TEST_URL="http://127.0.0.1:$NOP_PORT"
export NOP_API_TEST_USER=openapi-ci@example.test NOP_API_TEST_PASSWORD="$NOP_OPENAPI_ADMIN_PASSWORD"
scratch=$(mktemp -d)
compose=(docker compose -p "catalog-sql-$$" -f docker/compose.sqlserver.yaml)
cleanup() {
    "${compose[@]}" down --volumes >&2
    rm -r "$scratch"
}
trap cleanup EXIT
"${compose[@]}" up --build -d
python3 scripts/install-test-store.py
"${compose[@]}" restart web
curl --retry 40 --retry-delay 2 --retry-all-errors -fsS "$NOP_API_TEST_URL/api/swagger/v1/swagger.json" -o "$scratch/openapi.json"

export NOP_SQLSERVER_CONTAINER=$("${compose[@]}" ps -q db)
export SQLCMDPASSWORD="$NOP_DB_PASSWORD"
sql=(docker exec -i -e SQLCMDPASSWORD "$NOP_SQLSERVER_CONTAINER"
    /opt/mssql-tools18/bin/sqlcmd -S tcp:127.0.0.1,1433 -C -U sa -b -W -d nopcommerce)
"${sql[@]}" -Q "INSERT INTO Customer_CustomerRole_Mapping(Customer_Id,CustomerRole_Id)
    SELECT c.Id,r.Id FROM Customer c CROSS JOIN CustomerRole r
    WHERE c.Email='openapi-ci@example.test' AND r.SystemName='ApiUserRole'
    AND NOT EXISTS (SELECT 1 FROM Customer_CustomerRole_Mapping m WHERE m.Customer_Id=c.Id AND m.CustomerRole_Id=r.Id);"
"${sql[@]}" < docs/catalog-sync-sqlserver-preflight.sql > "$scratch/before.txt"
python3 scripts/test-sync-lost-response.py
python3 scripts/test-sqlserver-sync.py
"${sql[@]}" < docs/catalog-sync-sqlserver-preflight.sql > "$scratch/after.txt"
cmp "$scratch/before.txt" "$scratch/after.txt"
