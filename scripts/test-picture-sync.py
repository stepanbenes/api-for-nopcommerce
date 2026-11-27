#!/usr/bin/env python3
"""Exercise picture CAS and warmed thumbnails against a disposable local nopCommerce store."""

import argparse
import base64
from concurrent.futures import ThreadPoolExecutor
from contextlib import contextmanager
import hashlib
import html
import json
import os
from pathlib import Path, PurePosixPath
import re
import struct
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
import zlib


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--images-dir", type=Path, help="Run thumbnail failure recovery using the disposable shop's mounted images directory.")
images_dir = parser.parse_args().images_dir

BASE = os.environ["NOP_API_TEST_URL"].rstrip("/")
BASE_URL = urllib.parse.urlsplit(BASE)
if BASE_URL.scheme not in ("http", "https") or BASE_URL.hostname not in ("127.0.0.1", "localhost", "::1"):
    raise SystemExit("NOP_API_TEST_URL must point to a disposable loopback store.")


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


opener = urllib.request.build_opener(NoRedirect)


def request(method, path, auth=None, body=None, status=200, *, etag=None, capture_headers=False):
    url = urllib.parse.urljoin(BASE + "/", path)
    parts = urllib.parse.urlsplit(url)
    if (parts.scheme, parts.netloc) != (BASE_URL.scheme, BASE_URL.netloc):
        raise AssertionError("Test request would leave the configured loopback store")

    headers = {"Content-Type": "application/json", "Cache-Control": "no-cache"}
    if auth:
        headers["Authorization"] = "Bearer " + auth
    if etag:
        headers["If-None-Match"] = etag
    data = json.dumps(body).encode() if body is not None else None
    query = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        response = opener.open(query, timeout=15)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        expected = status if isinstance(status, tuple) else (status,)
        assert response.status in expected, f"{method} {path}: expected {expected}, got {response.status}"
        data = response.read()
        if capture_headers:
            return data, response.headers
        if response.headers.get_content_type() in ("application/json", "application/problem+json") and data:
            return json.loads(data)
        return data


def png(color):
    def chunk(kind, content):
        return struct.pack(">I", len(content)) + kind + content + struct.pack(">I", zlib.crc32(kind + content))

    width = height = 48
    pixels = (b"\x00" + bytes(color) * width) * height
    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(pixels))
        + chunk(b"IEND", b"")
    )


def sha(binary):
    return hashlib.sha256(binary).hexdigest()


def b64(binary):
    return base64.b64encode(binary).decode("ascii")


def desired(binary, expected=None):
    return {
        "expected_sha256": expected,
        "sha256": sha(binary),
        "mime_type": "image/png",
        "base64": b64(binary)
    }


@contextmanager
def blocked_synthetic_thumb(images_dir, url, picture_id, slug, original):
    path = PurePosixPath(urllib.parse.unquote(urllib.parse.urlsplit(url).path))
    prefix = f"{picture_id:07d}"
    filename = f"{prefix}_{slug}.png"
    if not re.fullmatch(r"picture-cas-[0-9a-f]{32}", slug) or path not in (
        PurePosixPath("/images/thumbs", filename),
        PurePosixPath("/images/thumbs", prefix[:2], filename),
    ):
        raise AssertionError("Refusing to fault a path outside the new synthetic picture")

    root = images_dir.resolve(strict=True)
    target = root.joinpath(*path.relative_to("/images").parts)
    if target.resolve(strict=True) != target or not target.is_file() or target.read_bytes() != original:
        raise AssertionError("The mounted thumbnail must be the original synthetic PNG")

    backup = target.with_name(f".{filename}.{uuid.uuid4().hex}.backup")
    if backup.exists() or backup.is_symlink():
        raise AssertionError("Synthetic thumbnail backup path already exists")

    target.rename(backup)
    blocked = False
    try:
        target.mkdir()
        blocked = True
        yield
    finally:
        if blocked:
            target.rmdir()
        backup.replace(target)


merchant_images = [png((25, 30, 50)), png((85, 90, 80))]
initial = png((255, 0, 0))
updated = png((0, 255, 0))
auth = request(
    "POST", "/token", body={
        "username": os.environ["NOP_API_TEST_USER"],
        "password": os.environ["NOP_API_TEST_PASSWORD"]
    }
)["access_token"]

name = f"Picture CAS {uuid.uuid4().hex}"
created = request("POST", "/api/products", auth, {
    "product": {
        "name": name,
        "published": True,
        "visible_individually": True,
        "price": 5
    }
})["products"][0]
product_path = f"/api/products/{created['id']}"
sync_path = product_path + "/pictures/sync/hero"
try:
    request("GET", sync_path, status=401)
    request("GET", "/api/products/2147483647/pictures/sync/hero", auth, status=404)
    guest = request("POST", "/token", body={"guest": True})
    try:
        request("GET", sync_path, guest["access_token"], status=403)
    finally:
        request("DELETE", f"/api/customers/{guest['customer_id']}", auth)

    assert request("GET", sync_path, auth)["status"] == "missing"
    for index, binary in enumerate(merchant_images):
        seed_path = product_path + f"/pictures/sync/fixture-{index}"
        assert request("PUT", seed_path, auth, desired(binary))["status"] == "added"
    gallery_before = request("GET", product_path, auth)["products"][0]["images"]
    assert len(gallery_before) == 2
    existing = gallery_before[0]
    linked = f"{product_path}/pictures/{existing['id']}/{existing['picture_id']}/sync"
    mismatched = f"{product_path}/pictures/{existing['id']}/{gallery_before[1]['picture_id']}/sync"
    request("GET", mismatched, auth, status=404)
    request("PUT", mismatched, auth, desired(initial), status=404)
    existing_digest = request("GET", linked, auth)
    assert existing_digest["status"] == "present"
    assert existing_digest["sha256"] == sha(merchant_images[0])
    assert request("PUT", linked, auth, {"sha256": existing_digest["sha256"]})["status"] == "unchanged"
    existing_src = existing["src"]
    assert request("GET", existing_src) == merchant_images[0]
    linked_change = request("PUT", linked, auth, desired(initial, existing_digest["sha256"]))
    assert linked_change == existing_digest | {"status": "replaced", "sha256": sha(initial)}
    assert request("GET", existing_src) == initial
    assert request("GET", product_path, auth)["products"][0]["images"] == gallery_before

    invalid = desired(initial) | {"sha256": sha(updated)}
    invalid_result = request("PUT", sync_path, auth, invalid, status=400)
    assert invalid_result["status"] == "invalid_image"
    assert request("GET", sync_path, auth)["status"] == "missing"

    with ThreadPoolExecutor(max_workers=2) as executor:
        attempts = [executor.submit(request, "PUT", sync_path, auth, desired(initial)) for _ in range(2)]
    added, unchanged = sorted((attempt.result() for attempt in attempts), key=lambda result: result["status"])
    assert added["status"] == "added" and added["sha256"] == sha(initial)
    assert unchanged == added | {"status": "unchanged"}
    assert request("PUT", sync_path, auth, desired(initial))["status"] == "unchanged"
    assert request("PUT", sync_path, auth, {"sha256": sha(initial)})["status"] == "unchanged"

    product = request("GET", product_path, auth)["products"][0]
    gallery = product["images"]
    assert gallery[:-1] == gallery_before
    image = gallery[-1]
    assert image["id"] == added["mapping_id"] and image["picture_id"] == added["picture_id"]
    url = image["src"]
    before, original_headers = request("GET", url, capture_headers=True)
    assert before == initial
    original_etag = original_headers["ETag"]
    assert original_etag

    # The public product page warms storefront-size thumbnails of this same picture.
    page = request("GET", "/" + urllib.parse.quote(product["se_name"])).decode()
    filename = urllib.parse.urlsplit(url).path.rsplit("/", 1)[-1]
    stem, ext = filename.rsplit(".", 1)
    sized_urls = set(re.findall(r'["\x27]([^"\x27]*?/images/thumbs/[^"\x27]*?'
                                + re.escape(stem) + r'_\d+\.' + re.escape(ext) + r')["\x27]', page))
    assert sized_urls, "The storefront page did not expose a warmed sized thumbnail"
    sized_before = {html.unescape(path): sha(request("GET", html.unescape(path))) for path in sized_urls}

    conflict = request("PUT", sync_path, auth, desired(updated, sha(b"stale")), status=409)
    assert conflict["status"] == "conflict" and conflict["sha256"] == sha(initial)
    replacement = desired(updated, sha(initial))
    if images_dir is None:
        replaced = request("PUT", sync_path, auth, replacement)
        assert replaced["status"] == "replaced"
    else:
        with blocked_synthetic_thumb(images_dir, url, added["picture_id"], product["se_name"], initial):
            replaced = request("PUT", sync_path, auth, replacement, status=503)
            assert replaced["status"] == "thumbnail_pending"
            current = request("GET", sync_path, auth)
            assert current["status"] == "present" and current["sha256"] == sha(updated)

        # Virtualized mounts can take a moment to expose the restored file to the shop.
        for _ in range(6):
            replay = request("PUT", sync_path, auth, {"sha256": sha(updated)}, status=(200, 503))
            if replay["status"] != "thumbnail_pending":
                break
            time.sleep(0.5)
        assert replay == replaced | {"status": "unchanged"}
        print("Recovered thumbnail_pending with a digest-only replay")

    assert replaced["sha256"] == sha(updated)
    assert replaced["mapping_id"] == added["mapping_id"] and replaced["picture_id"] == added["picture_id"]

    # Request the old URLs before requesting a new product or storefront page.
    assert request("GET", url, etag=original_etag) == updated
    for path, old_hash in sized_before.items():
        assert sha(request("GET", path)) != old_hash, path

    assert request("GET", sync_path, auth)["sha256"] == sha(updated)
    assert request("PUT", sync_path, auth, desired(updated, sha(initial)))["status"] == "unchanged"
    assert request("GET", product_path, auth)["products"][0]["images"] == gallery
    print(f"Verified existing and keyed digest, CAS, image IDs, gallery order and {len(sized_before) + 1} warmed URLs")
finally:
    request("DELETE", product_path, auth)
