# API plugin for nopCommerce 5.00

This plugin exposes REST endpoints for nopCommerce. This branch is built and tested against nopCommerce `release-5.00.1` and .NET 10. Use the matching release branch of this plugin for older versions of nopCommerce.

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

Configure each store's navigation under **Admin → Content management → Menus**. Omit `include_in_top_menu` from category and topic write payloads and omit `include_in_footer_column1`, `include_in_footer_column2`, and `include_in_footer_column3` from topic write payloads. A non-null value, including `false`, returns HTTP 400. The legacy properties are returned as `null` when included in reads.

The API account needs the **Api Users** role and the nopCommerce permission to manage menus. Use `GET /api/menus` to discover menus for the current store and `GET /api/menus/{menuId}/items` to list their links. Add a category or topic link with `POST /api/menus/{menuId}/items`. The body accepts `link_type` of `category` or `topic`, an `entity_id`, and an optional `display_order`, as in `{"link_type":"category","entity_id":42,"display_order":20}`. The add request returns HTTP 201 for a new link and 200 if the link already exists. Delete a link with `DELETE /api/menus/{menuId}/items/{itemId}`.

## Checkout attributes

Use `GET /api/checkout_attributes` to discover the checkout attributes and choices for the current store and cart. Add `?shipping_required=true` when preparing an order for shippable items before they are in the cart. Set `checkout_attributes` in the `order` body of `POST /api/orders` to supply the selections. Send one entry per checkout attribute, with the selected choice IDs as strings in its `values` array. For a dropdown such as Gift wrapping, send one value, for example `[{"id":1,"values":["1"]}]`. Product-level selections belong in each order item's `product_attributes`.

The JSON input accepts one choice for dropdowns, radio buttons, color squares, and image squares, and multiple choices for editable checkboxes. Single-line and multiline text fields accept one text value. Date pickers accept one `yyyy-MM-dd` value. For a file upload, send a multipart request to nopCommerce's `POST /uploadfilecheckoutattribute/{attributeId}` endpoint with the file as the first file field, then use the returned `downloadGuid` as the attribute's single value. Omit read-only checkboxes from the payload so nopCommerce applies the eligible preselected values.

Omit `checkout_attributes` to reuse the customer's saved cart selections. Add `include_conditional=true` to discovery to include dependent controls and their parent `condition`. A dependent control is eligible when the parent's complete selection matches the condition's `values` array.

For safe retries, send a non-empty UUID in the optional `Idempotency-Key` header on `POST /api/orders`. Reuse the same key for the same order attempt. A replay within the same store and customer returns the original order and includes `Idempotency-Replayed: true`. Use a new key for each new order. If payment follow-up fails after the order is saved, the API still returns the committed order and includes `X-Order-Follow-Up`. A retry whose original order has since been deleted returns HTTP 409.

## Tax categories

Use `GET /api/taxcategories/{id}` to read a tax category and `PUT /api/taxcategories/{id}` to update its supplied fields. A create request requires a nonblank name of at most 400 characters. Updates validate the name when it is supplied.

## Tests and generated client

Run the unit tests from this repository.

```sh
dotnet test Nop.Plugin.Api.Tests -p:NopRoot="$(cd ../nopCommerce && pwd)" --filter 'TestCategory!=Integration'
```

To exercise JWT authentication, endpoint permissions, API validation, and resource lifecycles against an installed local test store, set `NOP_API_TEST_URL`, `NOP_API_TEST_USER`, and `NOP_API_TEST_PASSWORD` in your shell, then run the integration category. The suite creates and deletes test catalog, cart, customer, order, topic, and tax records. The checkout test uses the Check/Money Order and Fixed By Weight By Total plugins with a Ground shipping option, as in the Docker sample store. It discovers and selects the sample Gift wrapping choice through the checkout attributes API.

```sh
dotnet test Nop.Plugin.Api.Tests -p:NopRoot="$(cd ../nopCommerce && pwd)" --filter 'TestCategory=Integration&TestCategory!=DatabaseIntegration'
dotnet run --project ClientApp -- "$NOP_API_TEST_URL"
```

With the [Docker stack](DOCKER.md) running and those API variables set, run the API-to-PostgreSQL cases from the same Compose project. They exercise tax category, product, cart item, and checkout writes against the loopback API and verify the resulting rows using read-only PostgreSQL queries. The checkout case checks that repeated requests with the same idempotency key create one order. The tests run `psql` inside the database container over its local socket. They use the Compose database and user `nopcommerce` and need no published PostgreSQL port or database password.

```sh
./docker/test-postgres.sh
```

The script requires Python 3 and succeeds only when all three cases run and pass. The cases clean up through the API. Tax category and cart item rows are removed. nopCommerce retains soft-deleted rows for the product and order.

The sample client requests a bearer token and reads catalog and country data. It generates its bindings from [the checked-in OpenAPI snapshot](ClientApp/nopapi.swagger.json). To refresh that snapshot after changing the API, download it from the installed test store, rebuild the client, and review the resulting diff.

```sh
curl --fail --silent --show-error "$NOP_API_TEST_URL/api/swagger/v1/swagger.json" -o ClientApp/nopapi.swagger.json
dotnet build ClientApp
```
