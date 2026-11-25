# API plugin for nopCommerce 5.00

This plugin exposes REST endpoints for nopCommerce. This branch is built and tested against nopCommerce `release-5.00.1` and .NET 10. Use the matching release branch of this plugin for older versions of nopCommerce.

The [versioned API documentation](https://stepanbenes.github.io/api-for-nopcommerce/) will be available once GitHub Pages is enabled. In the meantime, browse the released [v5.00.1 API reference in Swagger UI](https://petstore.swagger.io/?url=https%3A%2F%2Fraw.githubusercontent.com%2Fstepanbenes%2Fapi-for-nopcommerce%2Fc9456b59b4a4371b2c584f2f80fc849d222de318%2FClientApp%2Fnopapi.swagger.json&validatorUrl=none). See the [feature coverage and upstream issue audit](docs/api-coverage.md), [date and paging filters](docs/query-filters.md), and [checkout discovery endpoints](docs/checkout-discovery.md) for guidance on this branch.

## Install a release

1. Download `Nop.Plugin.Api.zip` and `Nop.Plugin.Api.zip.sha256` from [GitHub Releases](https://github.com/stepanbenes/api-for-nopcommerce/releases). Verify the download with `sha256sum -c Nop.Plugin.Api.zip.sha256` or `shasum -a 256 -c Nop.Plugin.Api.zip.sha256` on macOS.
2. Set `ApiConfiguration__SecurityKey` in the nopCommerce environment to a securely generated signing key, for example the output of `openssl rand -hex 32`.
3. In nopCommerce 5.00, open **Configuration → Local plugins → Upload plugin or theme** and upload `Nop.Plugin.Api.zip`. Follow the restart prompt and install **Api plugin** from **Local plugins**.
4. Assign the **Api Users** role and the required nopCommerce permissions to the API account.

GitHub Actions builds the installable ZIP against pinned nopCommerce `release-5.00.1` and runs the runtime OpenAPI and API integration tests with the plugin extracted from that ZIP. The workflow run provides the ZIP and checksum in the **nopcommerce-api-plugin** artifact. Push a version tag that matches the `Version` in `Nop.Plugin.Api/plugin.json`, prefixed with `v`. For example, `v5.00.1` publishes version `5.00.1` after the API and both generated client checks pass.

## Run with Docker

Follow the [Docker quickstart](DOCKER.md) to build the plugin and store together, install the database, and assign the API role.

## Build from source

1. Install the .NET SDK specified by the pinned nopCommerce checkout's `global.json`.
2. Clone the matching nopCommerce release in a sibling directory.

   ```sh
   git clone --depth 1 --branch release-5.00.1 https://github.com/nopSolutions/nopCommerce.git ../nopCommerce
   ```

3. Build the solution from this repository. `NopRoot` defaults to `../nopCommerce` relative to the repository root. Supply an absolute path to use another checkout.

   ```sh
   dotnet build api-for-nopcommerce.sln -p:NopRoot="$(cd ../nopCommerce && pwd)"
   ```

4. Configure the plugin's JWT signing key before starting the store. For a local session, set it to a generated value.

   ```sh
   export ApiConfiguration__SecurityKey="$(openssl rand -hex 32)"
   dotnet run --project ../nopCommerce/src/Presentation/Nop.Web/Nop.Web.csproj
   ```

5. Install the nopCommerce database if needed. Confirm **Api plugin** is installed under **Configuration → Local plugins** and assign the **Api Users** role to the API account. The account's nopCommerce permissions govern access to individual endpoints. Use an administrator with the **Api Users** role to run the integration suite.

The plugin build places its files in `src/Presentation/Nop.Web/Plugins/Nop.Plugin.Api` in the nopCommerce checkout. Swagger UI is served at `/api/swagger` and the OpenAPI document is served at `/api/swagger/v1/swagger.json`. Request an access token with `POST /token` and pass it as `Authorization: Bearer <token>` when calling protected endpoints.

## Navigation menus in 5.00

Use `GET /api/menus` to discover menus for the current store and `GET /api/menus/{menuId}/items` to list their links. Add a category or topic link with `POST /api/menus/{menuId}/items`. The body accepts `link_type` of `category` or `topic`, an `entity_id`, and an optional `display_order`. For example, send `{"link_type":"category","entity_id":42,"display_order":20}`. The add request returns HTTP 201 for a new link and 200 if the link already exists. Delete a link with `DELETE /api/menus/{menuId}/items/{itemId}`.

The API account needs the **Api Users** role and the nopCommerce permission to manage menus. You can also configure menus under **Admin → Content management → Menus**.

## Checkout attributes

Use `GET /api/checkout_attributes` to discover the checkout attributes and choices for the current store and cart. Add `?shipping_required=true` when preparing an order for shippable items before they are in the cart. Set `checkout_attributes` in the `order` body of `POST /api/orders` to supply the selections. Send one entry per checkout attribute, with the selected choice IDs as strings in its `values` array. For a dropdown such as Gift wrapping, send one value, for example `[{"id":1,"values":["1"]}]`. Product-level selections belong in each order item's `product_attributes`.

The JSON input accepts one choice for dropdowns, radio buttons, color squares, and image squares, and multiple choices for editable checkboxes. Single-line and multiline text fields accept one text value. Date pickers accept one `yyyy-MM-dd` value. For a file upload, send a multipart request to nopCommerce's `POST /uploadfilecheckoutattribute/{attributeId}` endpoint with the file as the first file field, then use the returned `downloadGuid` as the attribute's single value. Omit read-only checkboxes from the payload so nopCommerce applies the eligible preselected values.

Omit `checkout_attributes` to reuse the customer's saved cart selections. Add `include_conditional=true` to discovery to include dependent controls and their parent `condition`. A dependent control is eligible when the parent's complete selection matches the condition's `values` array.

For safe retries, send a non-empty UUID in the optional `Idempotency-Key` header on `POST /api/orders`. Reuse the same key for the same order attempt. A replay within the same store and customer returns the original order and includes `Idempotency-Replayed: true`. Use a new key for each new order. If payment follow-up fails after the order is saved, the API still returns the committed order and includes `X-Order-Follow-Up`. A retry whose original order has since been deleted returns HTTP 409.

## Tax categories

Use `GET /api/taxcategories/{id}` to read a tax category and `PUT /api/taxcategories/{id}` to update its supplied fields. A create request requires a nonblank name of at most 400 characters. Updates validate the name when it is supplied.

## Shipments

Store operators can list and create shipments, update tracking, and mark shipments as shipped, ready for pickup, or delivered. See the [shipment API routes and request examples](Shipments.md).

## Clear the nopCommerce cache

Send `POST /api/admin/cache/clear` with a bearer token and an empty request body to clear the shared nopCommerce static cache. This is the same cache cleared by **Clear cache** in nopCommerce administration and can affect all stores sharing that cache.

The API account needs the **Api Users** role and the `System.ManageMaintenance` permission. The endpoint records the `Api.ClearCache` activity before clearing the cache and returns HTTP 204 with an empty response on success. It returns HTTP 401 when authentication is required, HTTP 403 when access is denied, and HTTP 500 if it cannot record the activity.

## Tests and generated client

Run the unit tests from this repository. The .NET 10 suite covers services, serializers, validators, and query binding. The legacy controller, model-binder, and mapping sources remain outside the build. Live tests exercise the HTTP controller and binding paths against an installed store.

```sh
dotnet test Nop.Plugin.Api.Tests -p:NopRoot="$(cd ../nopCommerce && pwd)" --filter 'TestCategory!=Integration'
```

To exercise JWT authentication, endpoint permissions, API validation, and resource lifecycles against an installed local test store, set `NOP_API_TEST_URL`, `NOP_API_TEST_USER`, and `NOP_API_TEST_PASSWORD` in your shell, then run the integration category. The suite creates and deletes test catalog, cart, customer, order, topic, and tax records. The checkout test uses the Check/Money Order and Fixed By Weight By Total plugins with a Ground shipping option, as in the Docker sample store. It discovers and selects the sample Gift wrapping choice through the checkout attributes API.

```sh
dotnet test Nop.Plugin.Api.Tests -p:NopRoot="$(cd ../nopCommerce && pwd)" --filter 'TestCategory=Integration&TestCategory!=DatabaseIntegration'
dotnet run --project ClientApp -- "$NOP_API_TEST_URL"
```

With the [Docker stack](DOCKER.md) running and those API variables set, run the API-to-PostgreSQL cases from the same Compose project. The tests exercise tax category, product, cart item, checkout, and cache-clear operations against the loopback API and verify the resulting rows using read-only PostgreSQL queries. The checkout case checks that repeated requests with the same idempotency key create one order. The cache-clear case verifies that the audit entry identifies the authorized caller. The tests run `psql` inside the database container over its local socket. They use the Compose database and user `nopcommerce` and need no published PostgreSQL port or database password.

```sh
./docker/test-postgres.sh
```

The script requires Python 3 and succeeds only when all four cases run and pass. The cases clean up test entities through the API. Tax category and cart item rows are removed. nopCommerce retains soft-deleted rows for the product and order and keeps the cache-clear audit entry.

### Place an order with the generated C# client

The read-only example runs with just a store URL. To place an order in the Docker sample store, sign in as an API-enabled customer with saved billing and shipping addresses. Add a published shippable product that requires no product options to that customer's cart. For example, use `POST /api/shopping_cart_items` with `{"shopping_cart_item":{"customer_id":42,"product_id":8,"shopping_cart_type":"ShoppingCart","quantity":1}}` and replace the IDs with the customer's and product's IDs. You can create the addresses with `POST /api/customers/{customerId}/billingaddress` and `POST /api/customers/{customerId}/shippingaddress`, then use the IDs from their responses. Query `GET /api/checkout_attributes?shipping_required=true` to find the Gift wrapping attribute and its **No** choice. The two IDs below are illustrative.

```sh
checkout_key="$(uuidgen)"
dotnet run --project ClientApp -- "$NOP_API_TEST_URL" checkout BILLING_ADDRESS_ID SHIPPING_ADDRESS_ID "$checkout_key" 1=1
```

The example queries available shipping and payment methods for the cart and addresses, selects **Ground** and **Check/Money Order**, and places the cart's contents. It prints the committed order ID, payment and shipping statuses, and any follow-up guidance. If the response is lost, rerun with `checkout-retry` and the same key and addresses. This resubmits the original options without quoting an already purchased cart. Use a new key for each new order. Check/Money Order payment is recorded separately when received.

For stores with different methods, configure `PaymentMethodSystemName`, `ShippingMethod`, and `ShippingRateComputationMethodSystemName` in the request. A customer cart with items can query `GET /api/checkout/shipping-options?shipping_address_id=ID` and `GET /api/checkout/payment-methods?billing_address_id=ID` to find the methods available for its addresses. Use `GET /api/orders/{id}` to check final status. A store operator with shipment permissions can track [shipments](Shipments.md) separately.

The sample client requests a bearer token and reads catalog and country data. It generates its bindings from [the checked-in OpenAPI snapshot](ClientApp/nopapi.swagger.json). The client-generation checks run on GitHub branch pushes and pull requests and can also be triggered manually.

Run the same checks locally with the .NET 10 SDK and Python 3.12. Both generate from the checked-in snapshot. The C# tests compile the generated client, match its methods to all OpenAPI operation IDs, and check checkout JSON, authentication, and response decoding against a disposable loopback HTTP server. The Python check generates and imports the client, checks operation coverage, and verifies request and response serialization with HTTPX’s mock transport. These checks require only this repository and the public dependency feeds.

```sh
dotnet test ClientApp.Tests/ClientApp.Tests.csproj --configuration Release
bash scripts/test-python-client.sh
```

The **Runtime OpenAPI snapshot** job installs a fresh sample store and PostgreSQL database from the pinned nopCommerce release. It checks the live OpenAPI against the snapshot, runs the full Release API suite, and requires every case to pass, including all four PostgreSQL cases. To run that job locally, install the SDK listed in the upstream checkout's `global.json` and use Docker Compose and Python 3.

```sh
NOP_ROOT="$(cd ../nopCommerce && pwd)" bash scripts/check-runtime-openapi.sh --test-api
```

The script generates temporary credentials and removes its dedicated Compose project and volumes on exit.

To refresh the OpenAPI snapshot after changing the API, download it from the installed test store, run the client-generation checks, and review the resulting diff.

```sh
curl --fail --silent --show-error "$NOP_API_TEST_URL/api/swagger/v1/swagger.json" -o ClientApp/nopapi.swagger.json
dotnet test ClientApp.Tests/ClientApp.Tests.csproj --configuration Release
bash scripts/test-python-client.sh
```

## Preview or publish the API documentation

The [API documentation workflow](https://github.com/stepanbenes/api-for-nopcommerce/actions/workflows/api-docs.yml?query=event%3Apull_request) uploads an **api-docs-preview** artifact for pull requests. Download and extract it, run `python3 -m http.server --directory extracted-directory 8000` with the path to your extracted directory, and open `http://localhost:8000/unreleased/`. The preview includes its renderer and works offline.

Fetch the full repository history and its tags before building the preview locally.

```sh
python3 -m pip install Markdown==3.10.2
python3 scripts/build-docs.py --preview site
python3 -m http.server --directory site 8000
```

To host the site, select **GitHub Actions** under **Settings → Pages → Build and deployment → Source**. The workflow deploys the released references on pushes to the default branch and after stable releases are published. If the `github-pages` environment restricts deployments, allow the default branch and release tags (`v*`). Use **Run workflow** from the default branch to rebuild the site manually.
