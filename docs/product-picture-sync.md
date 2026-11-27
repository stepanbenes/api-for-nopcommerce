# Product picture synchronization

Use `GET /api/products/{productId}/pictures/sync/{key}` to compare a single integration-owned picture. Supply a stable caller key for each picture such as `hero` or `side-left`. Keys are unique per product and accept 1–64 lowercase ASCII letters, digits, underscores, and hyphens.

The API requires a bearer token with product edit permission. It applies the current store, customer ACL, and vendor restrictions. A product outside that scope returns `404`.

## Use an existing picture link

If the integration already knows its nopCommerce picture ID, match it to the corresponding gallery mapping ID from `GET /api/products/{productId}?fields=images`. Then use `GET` and `PUT /api/products/{productId}/pictures/{mappingId}/{pictureId}/sync` with the same digest protocol. For example, product `17`, gallery mapping `201`, and picture `302` use `/api/products/17/pictures/201/302/sync`. The three IDs must resolve to the same existing association. An unmatched tuple returns `404`, and an existing picture shared by multiple associations cannot be replaced. The existing-ID route preserves the link and does not create an integration key.

## Read the current digest

A new key returns `200` with status `missing`. When a key is present, the API reads the original bytes through nopCommerce and returns a lowercase SHA-256 digest, the product-picture mapping ID, and the picture ID. The GET response contains no image payload and does not generate thumbnails.

```json
{
  "status": "present",
  "mapping_id": 201,
  "picture_id": 302,
  "sha256": "a3ea16ff388070c7063adbf94a6ea91f19400c27469005f9b71c11f613237843"
}
```

The caller can skip the write entirely when the digest already matches its source.

## Add or replace bytes

Send `PUT /api/products/{productId}/pictures/sync/{key}` with the desired digest and base64 bytes. Use `expected_sha256: null` to add an absent picture, or the digest from the last read to replace it. For example, this adds a 1 × 1 PNG.

```json
{
  "expected_sha256": null,
  "sha256": "b1ff9c8ea3a780bad09b346c423d2d0e46815926879b18e841d928376a946640",
  "mime_type": "image/png",
  "base64": "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC"
}
```

| HTTP status | Result | Meaning |
| --- | --- | --- |
| 200 | `added` | The new picture is appended to the gallery. Its key remains associated with its mapping. |
| 200 | `replaced` | The original bytes and existing local thumbnails are refreshed at the existing URLs. Picture ID, mapping ID, display order, and alt and title attributes are preserved. |
| 200 | `unchanged` | The stored bytes already match `sha256`. The expected digest can be stale from an earlier attempt. The caller may omit `mime_type` and `base64`. |
| 400 | `invalid_image` | The payload is missing or invalid, the decoded digest differs, or the image falls outside the supported size or format. |
| 409 | `conflict` | The current digest differs from both digests supplied. Read the current state before retrying. |
| 409 | `ambiguous` | The key, mapping, or picture no longer resolves to one intact, exclusive product-picture association. |
| 503 | `unsupported_storage` or `busy` | A write requires the single-instance declaration, transactional database storage of originals, and the built-in local thumbnail provider. A `busy` response can be retried with the same digests. |
| 503 | `thumbnail_pending` | The original was saved, and refreshing a previously warmed local thumbnail failed. Retry the same PUT to finish thumbnail refresh. |

The write accepts JPEG and PNG originals up to 4 MiB and the shop's configured maximum image dimension. Resize oversized input before calculating its digest. A replacement must use the current MIME type so the existing thumbnail URLs keep their extensions. nopCommerce validates the original before writing, and the digest is verified against the bytes read back from storage.

Set `NOP_API_PICTURE_SYNC_LOCAL_THUMBS=1` on the shop process only for a single application instance with the built-in local thumbnail provider. Digest reads and completed no-op retries remain available without the setting.

The shop refreshes previously warmed URLs and scans again after the original commits to find thumbnails generated during the write. A storefront request already generating an old thumbnail can still race this scan. Browser and CDN caches follow the shop’s configured static-file cache policy. Request revalidation to fetch the new bytes from the existing URL.

The integration key is stored as a nopCommerce product attribute using the existing generic attribute service. Pictures added through the regular shop or product API have no integration key. Adding or replacing a keyed picture preserves other gallery images and their order. A shop administrator who removes a keyed mapping causes later requests for that key to report an ownership conflict.

Run the loopback acceptance check on a disposable installed store using the existing `NOP_API_TEST_URL`, `NOP_API_TEST_USER`, and `NOP_API_TEST_PASSWORD` environment variables.

```sh
python3 scripts/test-picture-sync.py
```

To simulate a thumbnail write failure, provide the disposable shop's local images directory. The test checks that the source digest commits, restores the synthetic thumbnail path, and verifies the same-digest retry repairs the warmed URLs.

```sh
python3 scripts/test-picture-sync.py --images-dir "/path/to/disposable/shop/wwwroot/images"
```
