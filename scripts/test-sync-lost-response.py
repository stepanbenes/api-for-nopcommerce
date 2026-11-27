"""Drop a committed HTTP response on a loopback fixture, then exercise readback and retry."""

import http.client
import json
import os
import socket
import threading
import uuid
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import urlsplit

base = urlsplit(os.environ["NOP_API_TEST_URL"])
if base.scheme != "http" or base.hostname not in {"localhost", "127.0.0.1"}:
    raise SystemExit("Use a disposable loopback HTTP store")
headers = {"Content-Type": "application/json"}


def api(method, path, body=None):
    conn = http.client.HTTPConnection(base.hostname, base.port, timeout=30)
    try:
        conn.request(method, path, json.dumps(body) if body is not None else None, headers)
        reply = conn.getresponse()
        data = reply.read()
        if reply.status != 200:
            raise RuntimeError(f"{method} {path} returned {reply.status}")
        return json.loads(data) if data else None
    finally:
        conn.close()


class DropResponse(BaseHTTPRequestHandler):
    def do_POST(self):
        payload = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        self.server.committed = api("POST", "/api/products/desired-price-stock", payload)
        self.connection.shutdown(socket.SHUT_RDWR)
        self.connection.close()


def main():
    token = api("POST", "/token", {"username": os.environ["NOP_API_TEST_USER"],
                                   "password": os.environ["NOP_API_TEST_PASSWORD"]})
    headers["Authorization"] = "Bearer " + token["access_token"]
    created = api("POST", "/api/products", {"product": {"name": "Lost response " + uuid.uuid4().hex, "price": 10}})
    product_id = created["products"][0]["id"]
    path = f"/api/products/{product_id}"
    update = {"items": [{"product_id": product_id, "expected_price": 10, "price": 11}]}
    try:
        with HTTPServer(("127.0.0.1", 0), DropResponse) as proxy:
            thread = threading.Thread(target=proxy.handle_request)
            thread.start()
            conn = http.client.HTTPConnection("127.0.0.1", proxy.server_port, timeout=40)
            try:
                conn.request("POST", "/", json.dumps(update), {"Content-Type": "application/json"})
                conn.getresponse()
                raise AssertionError("Proxy delivered an HTTP response")
            except http.client.RemoteDisconnected:
                pass
            finally:
                conn.close()
                thread.join()
            assert proxy.committed["results"] == [{"product_id": product_id, "status": "updated"}]
        before = api("GET", path)["products"][0]
        assert before["price"] == 11
        repeated = api("POST", "/api/products/desired-price-stock", update)
        assert repeated["results"] == [{"product_id": product_id, "status": "unchanged"}]
        after = api("GET", path)["products"][0]
        assert after["updated_on_utc"] == before["updated_on_utc"]
        print("Passed dropped-response recovery, readback and timestamp check")
    finally:
        api("DELETE", path)


if __name__ == "__main__":
    main()
