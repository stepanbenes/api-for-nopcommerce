# Checkout discovery

Both endpoints require a bearer token and use the customer's shopping cart for
the store serving the API request. An administrator with order write permission
can add `customer_id` to discover options for another customer's cart in that
same store.

- `GET /api/checkout/shipping-options?shipping_address_id=123` takes a shipping
  address ID from the cart owner's address book. Each entry in `shipping_options`
  contains `name`, `shipping_rate_computation_method_system_name`, and `rate`.
  The rate is the provider quote in the primary store currency, before checkout
  shipping adjustments, discounts, and tax. A cart that requires no shipping
  returns an empty array.
- `GET /api/checkout/payment-methods?billing_address_id=123` returns
  `payment_methods` containing `name` and `system_name`. Supply the billing
  address ID you plan to use on the order so nopCommerce can apply payment
  restrictions by country. Omitting it uses the customer's saved billing address.
  If country selection is disabled or the billing country is unspecified,
  payment plugins receive no country filter. A country on an existing address
  must still allow billing or shipping for the corresponding operation.
  Discovery returns eligible payment candidates when shipping, checkout charges,
  or a different billing address can affect the final total. A non-shippable cart
  with a confirmed zero total returns an empty array.

For `POST /api/orders`, use the same address IDs and copy the identifiers
from the chosen entries into the `order` object.

| Value | Order field |
| --- | --- |
| Shipping address ID | `shipping_address.id` |
| Billing address ID | `billing_address.id` |
| Shipping quote `name` | `shipping_method` |
| Shipping quote `shipping_rate_computation_method_system_name` | `shipping_rate_computation_method_system_name` |
| Payment entry `system_name` | `payment_method_system_name` |

Order submission resolves the shipping option again and calculates the final
amount. Native order placement determines whether payment is required.
Discovery leaves the cart and its checkout selections untouched.
