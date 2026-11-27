"""Verify catalog compare-and-set and stock history on a disposable SQL Server shop."""

import http.client
import json
import os
import subprocess
import uuid
from concurrent.futures import ThreadPoolExecutor
from urllib.parse import urlsplit


def main():
    base = urlsplit(os.environ["NOP_API_TEST_URL"])
    if base.scheme != "http" or base.hostname not in {"localhost", "127.0.0.1"}:
        raise ValueError("Use a disposable loopback fixture")

    headers = {"Content-Type": "application/json"}

    def api(method, path, payload=None):
        connection = http.client.HTTPConnection(base.hostname, base.port, timeout=45)
        try:
            connection.request(method, path, None if payload is None else json.dumps(payload), headers)
            response = connection.getresponse()
            data = response.read()
            if response.status != 200:
                raise RuntimeError(f"{method} {path} returned {response.status}")
            return json.loads(data) if data else None
        finally:
            connection.close()

    token = api("POST", "/token", {"username": os.environ["NOP_API_TEST_USER"],
                                   "password": os.environ["NOP_API_TEST_PASSWORD"]})
    headers["Authorization"] = "Bearer " + token["access_token"]
    capabilities = api("GET", "/api/sync/capabilities")
    assert capabilities["desired_price_stock"] is True
    assert capabilities["versioned_bulk_price_stock"] is False

    product = api("POST", "/api/products", {"product": {
        "name": f"SQL Server concurrency {uuid.uuid4().hex}", "price": 12,
        "stock_quantity": 3, "manage_inventory_method_id": 1,
        "use_multiple_warehouses": False, "published": True,
    }})["products"][0]
    product_id = int(product["id"])

    def history_count():
        sql = f"SET NOCOUNT ON; SELECT COUNT(*) FROM StockQuantityHistory WHERE ProductId={product_id}"
        result = subprocess.check_output([
            "docker", "exec", "-e", "SQLCMDPASSWORD", os.environ["NOP_SQLSERVER_CONTAINER"],
            "/opt/mssql-tools18/bin/sqlcmd", "-S", "tcp:127.0.0.1,1433", "-C", "-U", "sa",
            "-d", "nopcommerce", "-b", "-h", "-1", "-W", "-Q", sql,
        ], env={**os.environ, "SQLCMDPASSWORD": os.environ["NOP_DB_PASSWORD"]}, text=True)
        return int(result.strip())

    def update(price):
        item = {"product_id": product_id, "expected_price": 12, "price": price,
                "expected_stock_quantity": 3, "stock_quantity": 9}
        result = api("POST", "/api/products/desired-price-stock", {"items": [item]})
        return result["results"][0]["status"]

    path = f"/api/products/{product_id}"
    try:
        initial_count = history_count()
        with ThreadPoolExecutor(max_workers=4) as workers:
            outcomes = list(workers.map(update, [15] * 4))
        assert sorted(outcomes) == ["unchanged"] * 3 + ["updated"], outcomes
        before = api("GET", path)["products"][0]
        assert (before["price"], before["stock_quantity"]) == (15, 9)
        assert update(15) == "unchanged"
        assert update(17) == "conflict"
        after = api("GET", path)["products"][0]
        assert after["updated_on_utc"] == before["updated_on_utc"]
        assert (after["price"], after["stock_quantity"]) == (15, 9)
        assert history_count() == initial_count + 1
    finally:
        api("DELETE", path)
    print("SQL Server concurrency, conflict, replay and stock history checks passed")


if __name__ == "__main__":
    main()
