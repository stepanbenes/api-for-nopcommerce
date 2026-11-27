# Product updates and relationship writes

## Partial product updates

Send only the product fields you own to `PUT /api/products/{id}`. This route requires the native **Catalog. Products. Create, edit, delete** permission. The existing `product` envelope applies.

```json
{
  "product": {
    "price": 45.00,
    "stock_quantity": 12
  }
}
```

A partial update preserves the product's publication status and any omitted tags, image mappings, and discount mappings. Manage category links through the category mapping routes. An omitted or null collection preserves existing values. An empty array clears the corresponding tags, images, or discounts. A supplied nonempty array retains the existing replacement behavior for that collection.

Replaying a request with the same scalar values, tag set, image IDs and positions, and discount IDs returns the current product without advancing its `updated_on_utc`. Uploading image bytes in the legacy `images` array creates a new picture. Use the dedicated picture synchronization route for retries that must preserve IDs when the bytes are unchanged. [Picture synchronization contract](product-picture-sync.md)

## Add or reorder a single product relationship

Each relationship uses its existing JSON envelope and response collection.

| Path | Request envelope | Additional native permission |
| --- | --- | --- |
| `/api/product_category_mappings` | `product_category_mapping` | Catalog. Categories. Create, edit, delete |
| `/api/product_manufacturer_mappings` | `product_manufacturer_mapping` | Catalog. Manufacturers. Create, edit, delete |
| `/api/productspecificationattributes` | `product_specification_attribute` | Catalog. Specification attributes. Create, edit, delete |

All three require the product edit permission. Send `POST` with `product_id` and the matching `category_id`, `manufacturer_id`, or `specification_attribute_option_id`. A specification mapping defaults to the option type. The identity for a custom specification value also includes its type and custom value.

A successful add or compatible replay returns 200 with the mapping ID. A replay with conflicting supplied mapping values or ambiguous preexisting mappings returns 409. Use `PUT {path}/{id}` with `display_order` in the same envelope to reorder the existing link. Replaying the same order skips the update. Use `DELETE {path}/{id}` to remove that mapping. Writes validate product ownership for vendor accounts and enforce the store and ACL scope of the product and any category or manufacturer. A missing or out-of-scope entity returns 404.

Relationship and menu writes use serializable database transactions. The API retries serialization conflicts and returns 409 if the contention persists. The routes call nopCommerce's native services, which publish their usual entity events during the transaction. The HTTP response and catalog activity log follow commit. The routes return 503 if transaction enlistment is unavailable or SQL Server's NoLock option is enabled. Database writers must preserve relationship identity when inserting links. A writer that inserts without checking for an existing row can create duplicates in nopCommerce's existing schema.

## Add or reorder a navigation menu link

Use `GET /api/menus` and `GET /api/menus/{menuId}/items` to discover menu and item IDs. All menu routes require the native **Content management. Menus. Create, edit, delete** permission.

- Send `POST /api/menus/{menuId}/items` with `link_type` (`category` or `topic`), `entity_id`, and optionally `display_order`. A new link returns 201. A compatible retry returns 200 with the existing ID. If the existing order differs from the supplied order, the response is 409.
- Send `PUT /api/menus/{menuId}/items/{itemId}` with `{"expected_display_order": 20, "display_order": 25}` to update one link. The 200 response contains the item in `menu_items`. A request for the current order returns 200 without a write. If the order differs from both the expected and desired values, the response is 409.
- Send `DELETE /api/menus/{menuId}/items/{itemId}` to remove the link. A link with children returns 409 to protect the child links.

The menu, item, and linked entity must be in the current store and ACL scope for an order update. An item in a different menu returns 404. The order update applies to the chosen item and leaves its hierarchy intact. The linked category or topic must be published and have a storefront URL. When creating a category through the API, supply `se_name` to create its URL slug.
