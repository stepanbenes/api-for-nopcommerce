# Date filters and collection paging

Date filters accept either the snake-case names shown in the endpoint docs, such as `created_at_min`, or the equivalent casing used by the OpenAPI clients, such as `CreatedAtMin`. For example:

- `created_at_min=2008-12-31`
- `CreatedAtMin=2008-12-31%2003:00`
- `created_at_min=2008-12-31T04:30:00%2B01:30`

Dates and timestamps without an offset are interpreted as UTC. An invalid, empty, or repeated filter returns HTTP 400 with an `errors` entry for that filter.

Paged collections still default to page 1 with up to 50 results. The usual explicit limit is 250. Administrators can pass `unpaged=true` to return every matching result on the following routes, subject to each route's existing permissions and store rules:

- `/api/categories`, `/api/customers`, `/api/customers/search`, `/api/manufacturers`, `/api/news_letter_subscriptions`
- `/api/orders`, `/api/orders/{orderId}/items`, `/api/products`, `/api/productattributes`
- `/api/product_category_mappings`, `/api/product_manufacturer_mappings`, `/api/product_warehouse_inventories`
- `/api/productspecificationattributes`, `/api/specificationattributes`, `/api/shopping_cart_items`, `/api/topics`

Omit both `page` and `limit` when using `unpaged=true`. A conflicting or invalid flag returns HTTP 400. An authenticated caller without the Administrators role gets HTTP 403. Unpaged responses are built in memory, so use paging or filters for large collections. The option applies to the listed collection routes and leaves count, lookup, and invoice operations unchanged.
