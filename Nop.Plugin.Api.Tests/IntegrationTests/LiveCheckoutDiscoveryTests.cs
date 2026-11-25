using System.Net;
using NUnit.Framework;
using TestAssert = NUnit.Framework.Assert;

namespace Nop.Plugin.Api.Tests.IntegrationTests;

public partial class LiveApiTests
{
    [Test]
    public async Task CheckoutDiscoveryReturnsConfiguredOptionsAndRejectsUnownedAddress()
    {
        await Send(HttpMethod.Get, "/api/checkout/payment-methods", HttpStatusCode.Unauthorized);
        await AuthenticateAdministrator();
        var roles = (await Send(HttpMethod.Get, "/api/customer_roles", HttpStatusCode.OK)).GetProperty("customer_roles");
        var customerId = await CreateCustomer(RoleId(roles, "Registered"));
        int? productId = null;

        try
        {
            var countries = (await Send(HttpMethod.Get, "/api/countries", HttpStatusCode.OK)).GetProperty("countries");
            var countryId = countries.EnumerateArray()
                .Single(country => country.GetProperty("two_letter_iso_code").GetString() == "US")
                .GetProperty("id").GetInt32();
            var address = await Send(HttpMethod.Post, $"/api/customers/{customerId}/billingaddress", HttpStatusCode.OK, new
            {
                first_name = "Checkout",
                last_name = "Discovery",
                email = "checkout-discovery@example.test",
                country_id = countryId,
                city = "Phoenix",
                address1 = "100 S Central Ave",
                zip_postal_code = "85004",
                phone_number = "6025550100"
            });
            var addressId = address.GetProperty("id").GetInt32();
            var product = await Send(HttpMethod.Post, "/api/products", HttpStatusCode.OK, new
            {
                product = new
                {
                    name = $"API checkout discovery {Guid.NewGuid():N}",
                    price = 12.50m,
                    published = true,
                    visible_individually = true,
                    is_ship_enabled = true
                }
            });
            productId = product.GetProperty("products")[0].GetProperty("id").GetInt32();
            await Send(HttpMethod.Post, "/api/shopping_cart_items", HttpStatusCode.OK, new
            {
                shopping_cart_item = new { customer_id = customerId, product_id = productId, shopping_cart_type = "ShoppingCart", quantity = 2 }
            });

            var shipping = $"/api/checkout/shipping-options?customer_id={customerId}&shipping_address_id=";
            var payment = $"/api/checkout/payment-methods?customer_id={customerId}&billing_address_id={addressId}";
            await Send(HttpMethod.Get, shipping + int.MaxValue, HttpStatusCode.NotFound);
            var quotes = await Send(HttpMethod.Get, shipping + addressId, HttpStatusCode.OK);
            var quote = quotes.GetProperty("shipping_options").EnumerateArray().Single(option =>
                option.GetProperty("shipping_rate_computation_method_system_name").GetString() == "Shipping.FixedByWeightByTotal" &&
                option.GetProperty("name").GetString() == "Ground");
            TestAssert.That(quote.GetProperty("rate").GetDecimal(), Is.GreaterThanOrEqualTo(0));
            var methods = await Send(HttpMethod.Get, payment, HttpStatusCode.OK);
            TestAssert.That(methods.GetProperty("payment_methods").EnumerateArray().Select(method =>
                method.GetProperty("system_name").GetString()), Does.Contain("Payments.CheckMoneyOrder"));
        }
        finally
        {
            try
            {
                if (productId.HasValue)
                {
                    await Send(HttpMethod.Delete, $"/api/shopping_cart_items?customer_id={customerId}", HttpStatusCode.OK);
                    await Send(HttpMethod.Delete, $"/api/products/{productId}", HttpStatusCode.OK);
                }
            }
            finally
            {
                await Send(HttpMethod.Delete, $"/api/customers/{customerId}", HttpStatusCode.OK);
            }
        }
    }
}
