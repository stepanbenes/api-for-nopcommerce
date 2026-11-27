# Catalog synchronization over HTTP

Use a service account assigned the Api Users role and the native permissions listed for each route. Exchange its credentials at `POST /token` and send the bearer token on API requests. Use the shop URL for the intended store. Keep the linked nopCommerce IDs and source ownership in the calling system.

## Select a write mode

| Installed API plugin | Client behavior |
| --- | --- |
| 5.00.4 | Versioned bulk requires its existing receipt table and unique index. This version predates HTTP capability discovery. |
| 5.00.5 on host 5.00.0 or 5.00.1 | Read `GET /api/sync/capabilities` at runtime. Use a true capability for the matching write. A failed probe or an unknown version requires operator configuration before selecting a writer. |

Choose one writer for each owned field. SQL Server writers and HTTP writers share product, inventory, and image state. Reconcile timeouts against the same account, store, and entity. A price or quantity that changes away and then back cannot establish version ordering without a durable receipt.

| Work | HTTP contract and recovery |
| --- | --- |
| Linked product price and stock | Product edit permission. Send 1–100 distinct product IDs. Use expected/desired pairs for receipt-free writes or reuse the exact operation ID, version, and payload for receipt-backed requests. Inspect each result before recording success. Legacy `updated` includes equal values. [Fields, statuses and readback](../../Products.md#catalog-write-capabilities) |
| Warehouse quantity | Product edit permission. Read the existing inventory row by ID. Send only expected and desired quantity. On timeout GET the row and compare. [Warehouse contract](../../Products.md#warehouse-stock) |
| Product pictures | Product edit permission. Use an existing product/mapping/picture tuple or a stable integration-owned key, one picture per PUT. Compare SHA-256 first. On timeout issue the same PUT to finish any pending thumbnail repair. A digest conflict needs reconciliation. [Scopes, formats and byte limits](../product-picture-sync.md) |
| Menu membership and order | Menu edit permission. Add one category/topic link per POST. Re-read membership on timeout or conflict. Identify a single item by menu and item ID to change its order. [Methods, request fields and status codes](../sync-product-relationships.md#add-or-reorder-a-navigation-menu-link) |
| Category, manufacturer, specification | Product edit and matching taxonomy edit permissions. Add one link per POST. Reconcile scope, identity and supplied fields on conflict. [Mapping keys and envelopes](../sync-product-relationships.md#add-or-reorder-a-single-product-relationship) |
| Partial product fields | Product edit permission. PUT one product. Omit unowned fields. Explicit empty arrays clear collections. Use the narrower stock/picture operations for concurrency and retry handling. [Omission and replacement](../sync-product-relationships.md#partial-product-updates) |
| Shared static cache | `POST /api/admin/cache/clear`, bearer plus native maintenance permission, empty body. Success is 204. A retry clears shared cache again and records another audit entry. Omit this call after a no-op. |

A synchronization client should record completion only after every requested item has a successful outcome or its state has been reconciled.

See [synthetic exchanges](fixtures.json) and the [OpenAPI specification](../../ClientApp/nopapi.swagger.json). Fixture IDs are illustrative and must be replaced with linked IDs from the target shop.

## Deploy and verify on a disposable SQL Server shop

1. Identify the initialized synthetic shop and its host version. Run the [SQL Server preflight](../catalog-sync-sqlserver-preflight.sql) on its existing database and save the output. Retain the current plugin package and shop configuration. The deployable API plugin contains no receipt migration.
2. Select the ZIP for that host, verify its SHA-256, then deploy its single `Nop.Plugin.Api` folder through the shop's normal plugin deployment. For picture writes on a single instance with database originals and local thumbnails, set `NOP_API_PICTURE_SYNC_LOCAL_THUMBS=1` on the web process. Restart and verify the installed API plugin reports 5.00.5. The ZIP build and host version are independent identifiers.
3. Retrieve `/api/swagger/v1/swagger.json` and authenticate to `/api/sync/capabilities`. SQL Server conditional writes require transaction enlistment and the shop's NoLock setting disabled. Configure access to the synthetic entities using the shop's store, vendor, and ACL rules. nopCommerce's ignore-ACL and ignore-store-limitation settings bypass their respective restrictions.
4. Set `NOP_API_TEST_URL`, `NOP_API_TEST_USER`, and `NOP_API_TEST_PASSWORD` for a disposable loopback fixture. Run `python scripts/test-sync-lost-response.py` and `python scripts/test-picture-sync.py` from the source checkout. Both create synthetic records and clean up the product on exit. The extra thumbnail fault test requires the fixture's mounted images directory. Re-run the SQL preflight after startup and after the tests, and compare the schema and migration fingerprints.
5. When the capability response enables versioned bulk, exercise the same operation twice, a stale version, a changed payload with the same version, and failure before commit on that specific provider. Where receipts are absent, select the desired-state route. No process should create a receipt table or write a migration-history marker to enable it.
