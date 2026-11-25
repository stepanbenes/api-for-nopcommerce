# nopCommerce 5.00 API coverage

This audit compares the fork's implemented routes with nopCommerce **5.00.1** and the open issues in the plugin's upstream repository, reviewed on 25 November 2026. An operation counts as covered when an HTTP client can perform it through this plugin. A database field in an order or product response establishes read access to that field only. The baseline at commit `417012f` exposed 124 operations across 72 paths. The current list of methods, request types and responses is in the [OpenAPI document](../ClientApp/nopapi.swagger.json).

### Migrating to this branch's API contract

The OpenAPI document on this branch removes legacy fields that the plugin could no longer read or persist on nopCommerce 5.00.1. This change applies when upgrading to a build from this branch. An existing plugin installation keeps the contract of its installed build.

| Removed fields | What clients can use |
| --- | --- |
| Category and topic `include_in_top_menu` and topic `include_in_footer_column1` through `include_in_footer_column3` | Writes ignore these fields and responses omit them. Manage links through the [menu API](../README.md#navigation-menus-in-500). |
| Product `special_price` and its two date fields, `has_tier_prices`, and `is_telecommunications_or_broadcasting_or_electronic_services` | Use `price` for the base catalog price. Manage tier prices and pricing rules in nopCommerce administration. |
| Product and category `has_discounts_applied` | Check whether `discount_ids` contains any entries. |
| Category and manufacturer `price_ranges` | Manage price-range filters in nopCommerce administration. |
| Store `secure_url` | Read `url` and `ssl_enabled`. |
| Order `reward_points_were_added` | Check the customer's reward-point history in nopCommerce administration. |
| Product attribute combination `picture_id` | Use `images` for product-level pictures. Manage combination-specific picture assignments in nopCommerce administration. |

Category `show_on_home_page` and order `pick_up_in_store` keep their existing JSON names. Responses now include their stored values.

### Where the API still has gaps

| Workflow | Coverage and remaining work |
| --- | --- |
| Payments and after-sales | Clients can place an order and read its payment status. Capture, void, full and partial refund, recurring-payment administration, and customer return requests have separate native workflows with no corresponding plugin operations. Updating a status field does not perform a payment-provider operation. [Upstream order administration](https://github.com/nopSolutions/nopCommerce/blob/release-5.00.1/src/Presentation/Nop.Web/Areas/Admin/Controllers/OrderController.cs) · [Returns](https://github.com/nopSolutions/nopCommerce/blob/release-5.00.1/src/Presentation/Nop.Web/Controllers/ReturnRequestController.cs) |
| Promotions and pricing | Product and order DTOs expose prices. Integrations still lack discount-rule management, gift-card issuance and redemption history, price-list administration, and dedicated coupon application/removal operations. [Discounts](https://github.com/nopSolutions/nopCommerce/blob/release-5.00.1/src/Presentation/Nop.Web/Areas/Admin/Controllers/DiscountController.cs) · [Gift cards](https://github.com/nopSolutions/nopCommerce/blob/release-5.00.1/src/Presentation/Nop.Web/Areas/Admin/Controllers/GiftCardController.cs) · [Price lists](https://github.com/nopSolutions/nopCommerce/blob/release-5.00.1/src/Presentation/Nop.Web/Areas/Admin/Controllers/PriceListController.cs) |
| Catalog, vendors and customer engagement | Products, categories, manufacturers, warehouse stock and product mappings are available. Product review submission/moderation, vendor management, affiliates and back-in-stock subscriptions lack dedicated endpoints. [Reviews](https://github.com/nopSolutions/nopCommerce/blob/release-5.00.1/src/Presentation/Nop.Web/Areas/Admin/Controllers/ProductReviewController.cs) · [Vendors](https://github.com/nopSolutions/nopCommerce/blob/release-5.00.1/src/Presentation/Nop.Web/Areas/Admin/Controllers/VendorController.cs) · [Stock subscriptions](https://github.com/nopSolutions/nopCommerce/blob/release-5.00.1/src/Presentation/Nop.Web/Controllers/BackInStockSubscriptionController.cs) |
| Content and notifications | Topics, menu links, and newsletter listing/deactivation have routes. Blog posts, campaigns, message templates and queued email administration do not. This release has no webhook subscription, delivery or retry API. [Admin controller inventory](https://github.com/nopSolutions/nopCommerce/tree/release-5.00.1/src/Presentation/Nop.Web/Areas/Admin/Controllers) |
| Store administration | The API lists stores, currencies, languages and customer roles. It does not offer full administrative CRUD for those resources, permission management, plugin installation, scheduled tasks, settings or reports. The availability of a native admin page alone does not grant remote API access to it. [Admin controller inventory](https://github.com/nopSolutions/nopCommerce/tree/release-5.00.1/src/Presentation/Nop.Web/Areas/Admin/Controllers) |

### How to interpret upstream issues

The [upstream tracker](https://github.com/stepanbenes/api-for-nopcommerce/issues) had 15 open issues at review time and no open pull requests. Five reports target older hosts: [4.40](https://github.com/stepanbenes/api-for-nopcommerce/issues/100), [4.70 installation](https://github.com/stepanbenes/api-for-nopcommerce/issues/97), [4.80 build](https://github.com/stepanbenes/api-for-nopcommerce/issues/101), [4.80 compatibility](https://github.com/stepanbenes/api-for-nopcommerce/issues/99), and [4.90 support](https://github.com/stepanbenes/api-for-nopcommerce/issues/108). This branch targets 5.00. The runtime CI job installs that version in a fresh store.

The [Swagger authorization report](https://github.com/stepanbenes/api-for-nopcommerce/issues/96) describes a 4.70 installation that required Facebook credentials. The 5.00 fork uses a configured JWT signing key, the Api Users role and nopCommerce permissions. The [test modernization request](https://github.com/stepanbenes/api-for-nopcommerce/issues/10) is covered by the .NET 10 unit, generated-client and installed-store integration suites. Run instructions are in the [README](../README.md).

### Changes prompted by the audit

| Upstream request | What the fork provides |
| --- | --- |
| [#87 · Edit shipment information](https://github.com/stepanbenes/api-for-nopcommerce/issues/87) | Order-scoped shipment listing, detail, partial shipments by order line, tracking number and admin comment updates, and ship, ready-for-pickup and deliver actions. |
| [#93 · How to complete an order from the generated client](https://github.com/stepanbenes/api-for-nopcommerce/issues/93) | Shipping quotes for an owned address and payment methods eligible for the cart. Use the selected provider identifiers when placing an order. See [checkout discovery](checkout-discovery.md) and the [C# example](../ClientApp/CheckoutExample.cs). |
| [#57 · Timestamps silently ignored](https://github.com/stepanbenes/api-for-nopcommerce/issues/57) | Date filtering accepts the documented space-separated timestamp and ISO timestamps with offsets. Invalid dates return a validation error. See [formats and examples](query-filters.md). |
| [#33 · Retrieve all matching records](https://github.com/stepanbenes/api-for-nopcommerce/issues/33) | Administrators can request `unpaged=true` on supported collections. The fork keeps the existing page size for clients that omit paging parameters. Large exports should request pages. See [supported collection routes](query-filters.md). |
| [#16 · Nullable enums in Swagger](https://github.com/stepanbenes/api-for-nopcommerce/issues/16) | The contract distinguishes enum values, JSON null and omission. Generated C# and Python clients exercise representative enum fields and filters. |
| [#7 · Nullable annotations](https://github.com/stepanbenes/api-for-nopcommerce/issues/7) | Public DTOs and response wrappers use nullable reference annotations. They remain mutable for partial updates and existing model binding. |
| [#6 · Investigate singleton dictionary](https://github.com/stepanbenes/api-for-nopcommerce/issues/6) | Removed the unused dependency-injection registration. Nested validation receives its request data explicitly. |
| [#2 · Asynchronous service queries](https://github.com/stepanbenes/api-for-nopcommerce/issues/2) | API services, paged queries and their controller callers await database reads. Database-side filtering and paging are especially important for catalog and customer lists. |
