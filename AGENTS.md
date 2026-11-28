# API plugin for nopCommerce

This repository contains the API plugin, its OpenAPI contract, generated-client
checks, and integration tests. Read `Nop.Plugin.Api/plugin.json` on the target
branch for the plugin version and supported host versions. Report the actual
nopCommerce host version separately from the plugin version.

Use the [CI workflow](.github/workflows/client-generation.yml) for the host pin
for each database job and the [README](README.md) for local setup. The plugin
build writes into the host checkout. Set the MSBuild property `NopRoot` when
building against a checkout outside the default location.

## Working on an endpoint

- Start with the controller, the nopCommerce service it calls, and the matching
  operation in `ClientApp/nopapi.swagger.json`. Check whether an existing route
  already supports the requested operation.
- Preserve published routes, operation IDs, response envelopes, and status
  meanings unless the change explicitly calls for a breaking API change.
- Check the native permission and the applicable customer ownership, store,
  vendor, and ACL scope. Apply the same scope to writes and readback. Filter
  lists before pagination.
- Use nopCommerce services for catalog mutations so stock history, reservations,
  publication rules, and cache invalidation stay consistent.
- Assign each new action an `ApiAudience` and update the
  [audience audit](docs/api-audiences.md) when an operation or its audience
  changes. Review the fields returned to shoppers, including nested objects.
  Audience labels guide documentation. The endpoint enforces access.

## Integration tests

Every new endpoint must ship with an integration test that calls its HTTP method
and route on an installed nopCommerce store backed by the disposable Docker
Compose PostgreSQL database. Add it to the appropriate
[live test fixture](Nop.Plugin.Api.Tests/IntegrationTests) so CI runs it.

- Use synthetic data and follow the fixture's cleanup pattern. Assert the HTTP
  status and response. For reads, check the result against known records. For
  writes, verify the persisted state and side effects. Use `PostgresProbe` when
  a direct database assertion is needed.
- Exercise invalid input and denied access where applicable, including a caller
  requesting another customer's data or a resource outside the caller's scope.
- For partial updates, test omitted, empty, and supplied values separately.
  For retryable writes, test an identical repeat and a lost response. Test
  stale requests when the operation accepts a version or expected value.

## Database compatibility

- For catalog synchronization, detect receipt support from the existing table
  and required unique index. Check storage capability independently of route
  availability in Swagger. Keep the receipt-free path usable on a fresh shop.
- Exercise provider-specific behavior on the affected database. For concurrent
  SQL Server stock updates, check the final quantity and count the stock-history
  rows. See the [catalog synchronization guide](docs/catalog-sync/README.md).
- When verifying that a deployment preserves the schema, finish host
  installation first. Capture the schema and migration ledger before installing
  the API plugin, then compare both after deployment and testing.

## Verification

- Run the tests that cover the change. Run the PostgreSQL regression suite from
  the repository root against the matching host checkout:

  ```sh
  NOP_ROOT="$(cd ../nopCommerce && pwd)" bash scripts/check-runtime-openapi.sh --test-api
  ```

  To test the packaged ZIP itself, also set `NOP_PLUGIN_PACKAGE_DIR` to a
  temporary output directory, as CI does. For SQL Server synchronization
  changes, run `bash docker/test-sqlserver.sh` with `NOP_ROOT` set to that
  job's host checkout.
- For public API changes, compare the running plugin's OpenAPI document with
  `ClientApp/nopapi.swagger.json`. Refresh the snapshot from the installed test
  store when the contract changes. Run both generated-client checks in the README.
- For Pages changes, run `python3 scripts/test-docs.py` and build the offline
  preview as described in the README.
- When reporting live test results, name the host version, plugin version,
  database provider, and tests actually run. Identify skipped cases and the
  fixture needed to exercise them.

## Pull requests and releases

- Keep public code, branch names, fixtures, examples, and PR descriptions
  generic. Use synthetic identifiers and omit private customer and deployment
  details.
- In the PR, describe the observable behavior, compatibility impact, and
  verification. Call out any deployment setting an operator must change.
- When asked to release, tag the merged upstream commit with `v` followed by
  the version in `Nop.Plugin.Api/plugin.json`. Verify the published ZIP's
  checksum and embedded version, and check documentation deployment separately.
