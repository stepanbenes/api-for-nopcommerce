using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NUnit.Framework;
using TestAssert = NUnit.Framework.Assert;

namespace Nop.Plugin.Api.Tests.IntegrationTests;

[TestFixture]
[Category("Integration")]
public class LiveApiTests
{
    private HttpClient _client;
    private string _username;
    private string _password;
    private int _customerId;

    [SetUp]
    public void SetUp()
    {
        var baseUrl = Environment.GetEnvironmentVariable("NOP_API_TEST_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
            TestAssert.Ignore("Set NOP_API_TEST_URL to an installed test store to run the live API tests.");

        _username = Environment.GetEnvironmentVariable("NOP_API_TEST_USER");
        _password = Environment.GetEnvironmentVariable("NOP_API_TEST_PASSWORD");
        TestAssert.That(_username, Is.Not.Null.And.Not.Empty, "Set NOP_API_TEST_USER.");
        TestAssert.That(_password, Is.Not.Null.And.Not.Empty, "Set NOP_API_TEST_PASSWORD.");

        _client = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(baseUrl)
        };
    }

    [TearDown]
    public void TearDown() => _client?.Dispose();

    [Test]
    public async Task TokenAndPermissionsAreEnforced()
    {
        await Send(HttpMethod.Get, "/api/products", HttpStatusCode.Unauthorized);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-test-token");
        await Send(HttpMethod.Get, "/api/products", HttpStatusCode.Unauthorized);
        _client.DefaultRequestHeaders.Authorization = null;

        await Send(HttpMethod.Post, "/token", HttpStatusCode.BadRequest, new { });
        await Authenticate(new { guest = true });
        var guestId = _customerId;
        try
        {
            await Send(HttpMethod.Get, "/api/products?limit=1", HttpStatusCode.OK);
            await Send(HttpMethod.Get, "/api/taxcategories", HttpStatusCode.Forbidden);
        }
        finally
        {
            _client.DefaultRequestHeaders.Authorization = null;
            await AuthenticateAdministrator();
            await Send(HttpMethod.Delete, $"/api/customers/{guestId}", HttpStatusCode.OK);
        }
    }

    [Test]
    public async Task AuthenticatedReadsAndValidation()
    {
        await AuthenticateAdministrator();

        var customer = await Send(HttpMethod.Get, "/api/customers/me", HttpStatusCode.OK);
        TestAssert.That(customer.GetProperty("customers")[0].GetProperty("id").GetInt32(), Is.EqualTo(_customerId));

        var products = await Send(HttpMethod.Get, "/api/products?limit=1&fields=id,name", HttpStatusCode.OK);
        TestAssert.That(products.GetProperty("products").GetArrayLength(), Is.LessThanOrEqualTo(1));
        var count = await Send(HttpMethod.Get, "/api/products/count", HttpStatusCode.OK);
        TestAssert.That(count.GetProperty("count").GetInt32(), Is.GreaterThanOrEqualTo(0));

        await Send(HttpMethod.Get, "/api/categories?limit=1", HttpStatusCode.OK);
        await Send(HttpMethod.Get, "/api/orders?limit=1", HttpStatusCode.OK);
        await Send(HttpMethod.Get, "/api/countries", HttpStatusCode.OK);
        await Send(HttpMethod.Get, "/api/shopping_cart_items/me", HttpStatusCode.OK);

        var invalidPage = await Send(HttpMethod.Get, "/api/products?page=0", HttpStatusCode.BadRequest);
        TestAssert.That(invalidPage.GetProperty("errors").TryGetProperty("page", out _), Is.True);
        await Send(HttpMethod.Get, "/api/products/0", HttpStatusCode.BadRequest);
        await Send(HttpMethod.Get, "/api/products/2147483647", HttpStatusCode.NotFound);
        await Send(HttpMethod.Post, "/api/products", HttpStatusCode.UnprocessableEntity, new { invalid = new { } });
    }

    [TestCase("categories", "categories", true)]
    [TestCase("products", "products", true)]
    [TestCase("manufacturers", "manufacturers", true)]
    [TestCase("productattributes", "product_attributes", true)]
    [TestCase("specificationattributes", "specification_attributes", true)]
    [TestCase("productspecificationattributes", "product_specification_attributes", true)]
    [TestCase("product_category_mappings", "product_category_mappings", true)]
    [TestCase("product_manufacturer_mappings", "product_manufacturer_mappings", true)]
    [TestCase("product_warehouse_inventories", "product_warehouse_inventories", true)]
    [TestCase("customers", "customers", true)]
    [TestCase("orders", "orders", true)]
    [TestCase("shopping_cart_items", "shopping_carts", true)]
    [TestCase("news_letter_subscriptions", "news_letter_subscriptions", true)]
    [TestCase("warehouses", "warehouses")]
    [TestCase("topics", "topics", true)]
    [TestCase("taxcategories", "tax_categories", true)]
    [TestCase("countries", "countries")]
    [TestCase("stateprovinces", "state_provinces")]
    [TestCase("customer_roles", "customer_roles")]
    [TestCase("stores", "stores")]
    [TestCase("languages", "languages")]
    [TestCase("currencies", "currencies")]
    public async Task CollectionEndpointReturnsItsEnvelope(string resource, string envelope, bool bounded = false)
    {
        await AuthenticateAdministrator();
        var path = $"/api/{resource}" + (bounded ? "?limit=1" : "");
        var collection = (await Send(HttpMethod.Get, path, HttpStatusCode.OK)).GetProperty(envelope);
        TestAssert.That(collection.ValueKind, Is.EqualTo(JsonValueKind.Array));
        if (bounded)
            TestAssert.That(collection.GetArrayLength(), Is.LessThanOrEqualTo(1));
    }

    [Test]
    public async Task CategoryCanBeCreatedListedReadPartiallyUpdatedAndDeleted()
    {
        await AuthenticateAdministrator();
        const string path = "/api/categories";
        await Send(HttpMethod.Post, path, HttpStatusCode.UnprocessableEntity,
            new { category = new { name = "" } });

        await Send(HttpMethod.Post, path, HttpStatusCode.BadRequest,
            new { category = new { name = "Legacy navigation", include_in_top_menu = false } });

        var name = $"API integration category {Guid.NewGuid():N}";
        var created = await Send(HttpMethod.Post, path, HttpStatusCode.OK,
            new { category = new { name, description = "Original description", published = true } });
        var id = created.GetProperty("categories")[0].GetProperty("id").GetInt32();
        var categoryPath = $"{path}/{id}";

        try
        {
            var listed = await Send(HttpMethod.Get, $"{path}?ids={id}&limit=1", HttpStatusCode.OK);
            TestAssert.That(listed.GetProperty("categories")[0].GetProperty("name").GetString(), Is.EqualTo(name));
            var count = await Send(HttpMethod.Get, $"{path}/count", HttpStatusCode.OK);
            TestAssert.That(count.GetProperty("count").GetInt32(), Is.GreaterThanOrEqualTo(1));

            await Send(HttpMethod.Put, categoryPath, HttpStatusCode.UnprocessableEntity,
                new { category = new { name = "" } });
            await Send(HttpMethod.Put, categoryPath, HttpStatusCode.BadRequest,
                new { category = new { include_in_top_menu = true } });
            await Send(HttpMethod.Put, categoryPath, HttpStatusCode.OK,
                new { category = new { name = name + " updated" } });
            var updated = await Send(HttpMethod.Get, categoryPath, HttpStatusCode.OK);
            var category = updated.GetProperty("categories")[0];
            TestAssert.That(category.GetProperty("name").GetString(), Is.EqualTo(name + " updated"));
            TestAssert.That(category.GetProperty("description").GetString(), Is.EqualTo("Original description"));
            TestAssert.That(category.GetProperty("include_in_top_menu").ValueKind, Is.EqualTo(JsonValueKind.Null));
        }
        finally
        {
            await Send(HttpMethod.Delete, categoryPath, HttpStatusCode.OK);
        }

        await Send(HttpMethod.Get, categoryPath, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TopicNavigationFlagsAreRejectedOnWrite()
    {
        await AuthenticateAdministrator();
        const string path = "/api/topics";
        var systemName = $"api-integration-topic-{Guid.NewGuid():N}";
        var rejected = await Send(HttpMethod.Post, path, HttpStatusCode.BadRequest,
            new { topic = new { system_name = systemName, title = "API integration topic", body = "Example", include_in_footer_column1 = false } });
        TestAssert.That(rejected.GetProperty("errors").TryGetProperty("menu", out _), Is.True);

        var created = await Send(HttpMethod.Post, path, HttpStatusCode.OK,
            new { topic = new { system_name = systemName, title = "API integration topic", body = "Example" } });
        var topicPath = $"{path}/{created.GetProperty("topics")[0].GetProperty("id").GetInt32()}";

        try
        {
            await Send(HttpMethod.Put, topicPath, HttpStatusCode.BadRequest,
                new { topic = new { include_in_top_menu = true } });
            await Send(HttpMethod.Put, topicPath, HttpStatusCode.OK,
                new { topic = new { title = "Updated integration topic" } });

            var updated = (await Send(HttpMethod.Get, topicPath, HttpStatusCode.OK)).GetProperty("topics")[0];
            TestAssert.That(updated.GetProperty("title").GetString(), Is.EqualTo("Updated integration topic"));
            TestAssert.That(updated.GetProperty("include_in_top_menu").ValueKind, Is.EqualTo(JsonValueKind.Null));
            TestAssert.That(updated.GetProperty("include_in_footer_column1").ValueKind, Is.EqualTo(JsonValueKind.Null));
        }
        finally
        {
            await Send(HttpMethod.Delete, topicPath, HttpStatusCode.OK);
        }
    }

    [Test]
    public async Task CustomerRoleUpdateIsIdempotentAndCanRemoveAnOptionalRole()
    {
        await AuthenticateAdministrator();
        var roles = (await Send(HttpMethod.Get, "/api/customer_roles", HttpStatusCode.OK)).GetProperty("customer_roles");
        var registered = RoleId(roles, "Registered");
        var apiUser = RoleId(roles, "ApiUserRole");
        var customerId = await CreateCustomer(registered);
        var path = $"/api/customers/{customerId}";

        try
        {
            var bothRoles = new { customer = new { role_ids = new[] { registered, apiUser } } };
            await Send(HttpMethod.Put, path, HttpStatusCode.OK, bothRoles);
            await Send(HttpMethod.Put, path, HttpStatusCode.OK, bothRoles);

            var updated = await Send(HttpMethod.Get, path, HttpStatusCode.OK);
            TestAssert.That(CustomerRoleIds(updated), Is.EquivalentTo(new[] { registered, apiUser }));

            await Send(HttpMethod.Put, path, HttpStatusCode.OK,
                new { customer = new { role_ids = new[] { registered } } });
            var updatedAgain = await Send(HttpMethod.Get, path, HttpStatusCode.OK);
            TestAssert.That(CustomerRoleIds(updatedAgain), Is.EqualTo(new[] { registered }));
        }
        finally
        {
            await Send(HttpMethod.Delete, path, HttpStatusCode.OK);
        }
    }

    [TestCase(false)]
    [TestCase(true, Category = "DatabaseIntegration")]
    public async Task CheckoutAcceptsAnItemWithoutAttributesAndPartialUpdatePreservesShipping(bool verifyDatabase)
    {
        var database = verifyDatabase ? PostgresProbe.ForConfiguredContainer(_client.BaseAddress) : null;
        const string readOrder = """
            SELECT jsonb_build_object('count', count(*), 'id', min("Id"), 'deleted', bool_and("Deleted"))
            FROM "Order" WHERE "CustomerId" = :'id'::integer
            """;
        const string readOrderItem = """
            SELECT jsonb_build_object('count', count(*), 'product_id', min("ProductId"), 'quantity', sum("Quantity"))
            FROM "OrderItem" WHERE "OrderId" = :'id'::integer
            """;
        await AuthenticateAdministrator();
        var roles = (await Send(HttpMethod.Get, "/api/customer_roles", HttpStatusCode.OK)).GetProperty("customer_roles");
        var customerId = await CreateCustomer(RoleId(roles, "Registered"));
        int? productId = null;

        try
        {
            var countries = (await Send(HttpMethod.Get, "/api/countries", HttpStatusCode.OK)).GetProperty("countries");
            var usa = countries.EnumerateArray()
                .Single(country => country.GetProperty("two_letter_iso_code").GetString() == "US")
                .GetProperty("id").GetInt32();
            var address = new
            {
                first_name = "API",
                last_name = "Integration",
                email = $"api-address-{Guid.NewGuid():N}@example.test",
                country_id = usa,
                city = "Phoenix",
                address1 = "100 S Central Ave",
                zip_postal_code = "85004",
                phone_number = "6025550100"
            };
            var billing = await Send(HttpMethod.Post, $"/api/customers/{customerId}/billingaddress", HttpStatusCode.OK, address);
            var addressId = billing.GetProperty("id").GetInt32();
            await Send(HttpMethod.Post, $"/api/customers/{customerId}/shippingaddress", HttpStatusCode.OK, address);

            var product = await Send(HttpMethod.Post, "/api/products", HttpStatusCode.OK, new
            {
                product = new
                {
                    name = $"API integration checkout {Guid.NewGuid():N}",
                    price = 12.50m,
                    published = true,
                    visible_individually = true,
                    is_ship_enabled = true
                }
            });
            productId = product.GetProperty("products")[0].GetProperty("id").GetInt32();
            await Send(HttpMethod.Post, "/api/shopping_cart_items", HttpStatusCode.OK, new
            {
                shopping_cart_item = new
                {
                    customer_id = customerId,
                    product_id = productId,
                    shopping_cart_type = "ShoppingCart",
                    quantity = 2
                }
            });

            var orderAddress = new
            {
                id = addressId,
                address.first_name,
                address.last_name,
                address.email,
                address.country_id,
                address.city,
                address.address1,
                address.zip_postal_code,
                address.phone_number
            };
            var attributes = await Send(HttpMethod.Get, "/api/checkout_attributes?shipping_required=true", HttpStatusCode.OK);
            var giftWrapping = attributes.GetProperty("checkout_attributes").EnumerateArray()
                .Single(attribute => attribute.GetProperty("name").GetString() == "Gift wrapping");
            TestAssert.That(giftWrapping.GetProperty("required").GetBoolean(), Is.True);
            var giftWrappingId = giftWrapping.GetProperty("id").GetInt32();
            var giftWrappingValue = giftWrapping.GetProperty("choices").EnumerateArray()
                .Single(choice => choice.GetProperty("name").GetString() == "No")
                .GetProperty("id").GetInt32().ToString(CultureInfo.InvariantCulture);
            var orderRequest = new
            {
                customer_id = customerId,
                billing_address = orderAddress,
                shipping_address = orderAddress,
                payment_method_system_name = "Payments.CheckMoneyOrder",
                shipping_rate_computation_method_system_name = "Shipping.FixedByWeightByTotal",
                shipping_method = "Ground",
                order_items = new[] { new { product_id = productId, quantity = 1 } },
                checkout_attributes = new[] { new { id = giftWrappingId, values = new[] { giftWrappingValue } } }
            };
            await Send(HttpMethod.Post, "/api/orders", HttpStatusCode.BadRequest, new
            {
                order = orderRequest with
                {
                    checkout_attributes = new[] { new { id = giftWrappingId, values = new[] { "2147483647" } } }
                }
            });

            await Send(HttpMethod.Post, "/api/orders", HttpStatusCode.BadRequest,
                new { order = orderRequest with { shipping_method = "Missing test shipping option" } });
            var cart = await Send(HttpMethod.Get, $"/api/shopping_cart_items?customer_id={customerId}", HttpStatusCode.OK);
            var cartItem = cart.GetProperty("shopping_carts").EnumerateArray().Single();
            TestAssert.That(cartItem.GetProperty("product_id").GetInt32(), Is.EqualTo(productId));
            TestAssert.That(cartItem.GetProperty("quantity").GetInt32(), Is.EqualTo(2));

            var idempotencyKey = Guid.NewGuid().ToString();
            async Task<(JsonElement Order, bool Replayed)> Submit(string key, HttpStatusCode expectedStatus)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
                request.Headers.Add("Idempotency-Key", key);
                request.Content = JsonContent.Create(new { order = orderRequest });

                using var response = await _client.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();
                TestAssert.That(response.StatusCode, Is.EqualTo(expectedStatus), content);
                if (response.StatusCode != HttpStatusCode.OK)
                    return default;

                using var document = JsonDocument.Parse(content);
                var replayed = response.Headers.TryGetValues("Idempotency-Replayed", out var values) && values.Single() == "true";
                return (document.RootElement.GetProperty("orders")[0].Clone(), replayed);
            }

            await Submit("not-a-uuid", HttpStatusCode.BadRequest);
            if (database is not null)
            {
                var stored = await database.ReadAsync(readOrder, customerId);
                TestAssert.That(stored.GetProperty("count").GetInt32(), Is.Zero);
            }

            var submissions = await Task.WhenAll(Submit(idempotencyKey, HttpStatusCode.OK), Submit(idempotencyKey, HttpStatusCode.OK));
            var order = submissions.Single(submission => !submission.Replayed).Order;
            var orderId = order.GetProperty("id").GetInt32();
            TestAssert.That(submissions.Single(submission => submission.Replayed).Order.GetProperty("id").GetInt32(), Is.EqualTo(orderId));
            var retry = await Submit(idempotencyKey, HttpStatusCode.OK);
            TestAssert.That(retry.Replayed, Is.True);
            TestAssert.That(retry.Order.GetProperty("id").GetInt32(), Is.EqualTo(orderId));
            if (database is not null)
            {
                var stored = await database.ReadAsync(readOrder, customerId);
                var storedItem = await database.ReadAsync(readOrderItem, orderId);
                TestAssert.Multiple(() =>
                {
                    TestAssert.That(stored.GetProperty("count").GetInt32(), Is.EqualTo(1));
                    TestAssert.That(stored.GetProperty("id").GetInt32(), Is.EqualTo(orderId));
                    TestAssert.That(stored.GetProperty("deleted").GetBoolean(), Is.False);
                    TestAssert.That(storedItem.GetProperty("count").GetInt32(), Is.EqualTo(1));
                    TestAssert.That(storedItem.GetProperty("product_id").GetInt32(), Is.EqualTo(productId));
                    TestAssert.That(storedItem.GetProperty("quantity").GetInt32(), Is.EqualTo(3));
                });
            }

            var orderPath = $"/api/orders/{orderId}";
            TestAssert.That(order.GetProperty("shipping_method").GetString(), Is.EqualTo("Ground"));
            TestAssert.That(order.GetProperty("checkout_attribute_description").GetString(), Does.Contain("Gift wrapping"));
            TestAssert.That(order.GetProperty("shipping_address").GetProperty("address1").GetString(), Is.EqualTo(address.address1));

            var items = await Send(HttpMethod.Get, $"{orderPath}/items?limit=1", HttpStatusCode.OK);
            var item = items.GetProperty("order_items").EnumerateArray().Single();
            TestAssert.That(item.GetProperty("product_id").GetInt32(), Is.EqualTo(productId));
            TestAssert.That(item.GetProperty("quantity").GetInt32(), Is.EqualTo(3));

            await Send(HttpMethod.Put, orderPath, HttpStatusCode.OK, new { order = new { customer_ip = "192.0.2.1" } });
            var read = await Send(HttpMethod.Get, orderPath, HttpStatusCode.OK);
            var updated = read.GetProperty("orders")[0];
            TestAssert.That(updated.GetProperty("customer_ip").GetString(), Is.EqualTo("192.0.2.1"));
            foreach (var property in new[] { "shipping_method", "shipping_status", "shipping_rate_computation_method_system_name" })
                TestAssert.That(updated.GetProperty(property).GetString(), Is.EqualTo(order.GetProperty(property).GetString()), property);

            TestAssert.That(updated.GetProperty("shipping_address").GetProperty("address1").GetString(), Is.EqualTo(address.address1));
            TestAssert.That(updated.GetProperty("billing_address").GetProperty("address1").GetString(), Is.EqualTo(address.address1));

            await Send(HttpMethod.Put, orderPath, HttpStatusCode.OK, new { order = new { shipping_address = orderAddress } });
            var snapshotted = await Send(HttpMethod.Get, orderPath, HttpStatusCode.OK);
            var shippingAddress = snapshotted.GetProperty("orders")[0].GetProperty("shipping_address");
            TestAssert.That(shippingAddress.GetProperty("id").GetInt32(), Is.Not.EqualTo(addressId));
            TestAssert.That(shippingAddress.GetProperty("address1").GetString(), Is.EqualTo(address.address1));

            await Send(HttpMethod.Delete, orderPath, HttpStatusCode.OK);
            await Submit(idempotencyKey, HttpStatusCode.Conflict);
            if (database is not null)
            {
                var stored = await database.ReadAsync(readOrder, customerId);
                TestAssert.That(stored.GetProperty("count").GetInt32(), Is.EqualTo(1));
                TestAssert.That(stored.GetProperty("deleted").GetBoolean(), Is.True);
            }
        }
        finally
        {
            try
            {
                var orders = await Send(HttpMethod.Get, $"/api/orders/customer/{customerId}", HttpStatusCode.OK);
                await TestAssert.MultipleAsync(async () =>
                {
                    foreach (var order in orders.GetProperty("orders").EnumerateArray())
                        await Send(HttpMethod.Delete, $"/api/orders/{order.GetProperty("id").GetInt32()}", HttpStatusCode.OK);
                });
            }
            finally
            {
                try
                {
                    if (productId.HasValue)
                        await Send(HttpMethod.Delete, $"/api/shopping_cart_items?customer_id={customerId}", HttpStatusCode.OK);
                }
                finally
                {
                    try
                    {
                        if (productId.HasValue)
                            await Send(HttpMethod.Delete, $"/api/products/{productId}", HttpStatusCode.OK);
                    }
                    finally
                    {
                        await Send(HttpMethod.Delete, $"/api/customers/{customerId}", HttpStatusCode.OK);
                    }
                }
            }
        }
    }

    [TestCase(false)]
    [TestCase(true, Category = "DatabaseIntegration")]
    public async Task ProductAndCartItemCanBeCreatedUpdatedAndDeleted(bool verifyDatabase)
    {
        var database = verifyDatabase ? PostgresProbe.ForConfiguredContainer(_client.BaseAddress) : null;
        await AuthenticateAdministrator();
        var name = $"API integration product {Guid.NewGuid():N}";
        var created = await Send(HttpMethod.Post, "/api/products", HttpStatusCode.OK,
            new { product = new { name, published = true, visible_individually = true, price = 12.50m } });
        var productId = created.GetProperty("products")[0].GetProperty("id").GetInt32();
        var productPath = $"/api/products/{productId}";
        const string readProduct = """
            SELECT jsonb_build_object('name', "Name", 'price', "Price", 'deleted', "Deleted")
            FROM "Product" WHERE "Id" = :'id'::integer
            """;
        const string readCartItem = """
            SELECT jsonb_build_object('customer_id', "CustomerId", 'product_id', "ProductId", 'quantity', "Quantity")
            FROM "ShoppingCartItem" WHERE "Id" = :'id'::integer
            """;
        int? cartItemId = null;

        try
        {
            if (database is not null)
            {
                var stored = await database.ReadAsync(readProduct, productId);
                TestAssert.Multiple(() =>
                {
                    TestAssert.That(stored.GetProperty("name").GetString(), Is.EqualTo(name));
                    TestAssert.That(stored.GetProperty("price").GetDecimal(), Is.EqualTo(12.50m));
                    TestAssert.That(stored.GetProperty("deleted").GetBoolean(), Is.False);
                });
            }

            var projected = await Send(HttpMethod.Get, productPath + "?fields=id,name", HttpStatusCode.OK);
            var product = projected.GetProperty("products")[0];
            TestAssert.That(product.GetProperty("name").GetString(), Is.EqualTo(name));
            TestAssert.That(product.EnumerateObject().Select(property => property.Name), Is.EquivalentTo(new[] { "id", "name" }));

            var updated = await Send(HttpMethod.Put, productPath, HttpStatusCode.OK,
                new { product = new { id = productId, name = name + " updated" } });
            TestAssert.That(updated.GetProperty("products")[0].GetProperty("name").GetString(), Is.EqualTo(name + " updated"));

            if (database is not null)
            {
                var stored = await database.ReadAsync(readProduct, productId);
                TestAssert.That(stored.GetProperty("name").GetString(), Is.EqualTo(name + " updated"));
            }

            var cart = await Send(HttpMethod.Post, "/api/shopping_cart_items", HttpStatusCode.OK,
                new { shopping_cart_item = new { customer_id = _customerId, product_id = productId, shopping_cart_type = "ShoppingCart", quantity = 2 } });
            cartItemId = cart.GetProperty("shopping_carts").EnumerateArray()
                .Single(item => item.GetProperty("product_id").GetInt32() == productId)
                .GetProperty("id").GetInt32();

            if (database is not null)
            {
                var stored = await database.ReadAsync(readCartItem, cartItemId.Value);
                TestAssert.That(stored.GetProperty("customer_id").GetInt32(), Is.EqualTo(_customerId));
                TestAssert.That(stored.GetProperty("product_id").GetInt32(), Is.EqualTo(productId));
                TestAssert.That(stored.GetProperty("quantity").GetInt32(), Is.EqualTo(2));
            }

            var itemPath = $"/api/shopping_cart_items/{cartItemId}";
            var updatedCart = await Send(HttpMethod.Put, itemPath, HttpStatusCode.OK,
                new { shopping_cart_item = new { id = cartItemId, quantity = 3 } });
            TestAssert.That(updatedCart.GetProperty("shopping_carts")[0].GetProperty("quantity").GetInt32(), Is.EqualTo(3));

            var deletedCartItemId = cartItemId.Value;
            if (database is not null)
            {
                var stored = await database.ReadAsync(readCartItem, deletedCartItemId);
                TestAssert.That(stored.GetProperty("quantity").GetInt32(), Is.EqualTo(3));
            }

            await Send(HttpMethod.Delete, itemPath, HttpStatusCode.OK);
            cartItemId = null;
            if (database is not null)
                TestAssert.That((await database.ReadAsync(readCartItem, deletedCartItemId)).ValueKind, Is.EqualTo(JsonValueKind.Null));
        }
        finally
        {
            try
            {
                if (cartItemId.HasValue)
                    await Send(HttpMethod.Delete, $"/api/shopping_cart_items/{cartItemId}", HttpStatusCode.OK);
            }
            finally
            {
                await Send(HttpMethod.Delete, productPath, HttpStatusCode.OK);
            }
        }

        await Send(HttpMethod.Get, productPath, HttpStatusCode.NotFound);
        if (database is not null)
        {
            var stored = await database.ReadAsync(readProduct, productId);
            TestAssert.That(stored.GetProperty("deleted").GetBoolean(), Is.True);
        }
    }

    [TestCase(false)]
    [TestCase(true, Category = "DatabaseIntegration")]
    public async Task TaxCategoryCanBeListedReadUpdatedAndDeleted(bool verifyDatabase)
    {
        var database = verifyDatabase ? PostgresProbe.ForConfiguredContainer(_client.BaseAddress) : null;
        await AuthenticateAdministrator();
        const string path = "/api/taxCategories";
        var tooLong = new string('x', 401);
        await Send(HttpMethod.Post, path, HttpStatusCode.UnprocessableEntity,
            new { taxCategory = new { display_order = 17 } });
        await Send(HttpMethod.Post, path, HttpStatusCode.UnprocessableEntity,
            new { taxCategory = new { name = " " } });
        await Send(HttpMethod.Post, path, HttpStatusCode.UnprocessableEntity,
            new { taxCategory = new { name = tooLong } });

        var name = $"API integration tax {Guid.NewGuid():N}";
        var created = await Send(HttpMethod.Post, path, HttpStatusCode.OK,
            new { taxCategory = new { name, display_order = 17 } });
        var id = created.GetProperty("tax_categories")[0].GetProperty("id").GetInt32();
        var categoryPath = $"{path}/{id}";
        const string readCategory = """
            SELECT jsonb_build_object('name', "Name", 'display_order', "DisplayOrder")
            FROM "TaxCategory" WHERE "Id" = :'id'::integer
            """;

        async Task VerifyStoredCategory(string expectedName, int expectedDisplayOrder)
        {
            if (database is null)
                return;

            var stored = await database.ReadAsync(readCategory, id);
            TestAssert.That(stored.GetProperty("name").GetString(), Is.EqualTo(expectedName));
            TestAssert.That(stored.GetProperty("display_order").GetInt32(), Is.EqualTo(expectedDisplayOrder));
        }

        try
        {
            await VerifyStoredCategory(name, 17);

            var categories = await Send(HttpMethod.Get, path + "?limit=1", HttpStatusCode.OK);
            TestAssert.That(categories.GetProperty("tax_categories").GetArrayLength(), Is.EqualTo(1));

            var single = await Send(HttpMethod.Get, categoryPath, HttpStatusCode.OK);
            TestAssert.That(single.GetProperty("tax_categories")[0].GetProperty("name").GetString(), Is.EqualTo(name));

            var rejected = await Send(HttpMethod.Put, categoryPath, HttpStatusCode.UnprocessableEntity,
                new { taxCategory = new { name = " " } });
            TestAssert.That(rejected.GetProperty("errors").TryGetProperty("name", out _), Is.True);
            await Send(HttpMethod.Put, categoryPath, HttpStatusCode.UnprocessableEntity,
                new { taxCategory = new { name = tooLong } });

            await VerifyStoredCategory(name, 17);
            await Send(HttpMethod.Put, categoryPath, HttpStatusCode.OK,
                new { taxCategory = new { display_order = 23 } });
            await VerifyStoredCategory(name, 23);
            var reordered = await Send(HttpMethod.Get, categoryPath, HttpStatusCode.OK);
            TestAssert.That(reordered.GetProperty("tax_categories")[0].GetProperty("name").GetString(), Is.EqualTo(name));

            var maxLengthName = name.PadRight(400, 'x');
            await Send(HttpMethod.Put, categoryPath, HttpStatusCode.OK,
                new { taxCategory = new { name = maxLengthName } });
            await VerifyStoredCategory(maxLengthName, 23);
            var updated = await Send(HttpMethod.Get, categoryPath, HttpStatusCode.OK);
            var category = updated.GetProperty("tax_categories")[0];
            TestAssert.That(category.GetProperty("name").GetString(), Is.EqualTo(maxLengthName));
            TestAssert.That(category.GetProperty("display_order").GetInt32(), Is.EqualTo(23));
        }
        finally
        {
            await Send(HttpMethod.Delete, categoryPath, HttpStatusCode.OK);
        }

        await Send(HttpMethod.Get, categoryPath, HttpStatusCode.NotFound);
        if (database is not null)
            TestAssert.That((await database.ReadAsync(readCategory, id)).ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    private async Task<int> CreateCustomer(int roleId)
    {
        var email = $"api-integration-{Guid.NewGuid():N}@example.test";
        var created = await Send(HttpMethod.Post, "/api/customers", HttpStatusCode.OK, new
        {
            customer = new { email, password = Guid.NewGuid().ToString("N"), role_ids = new[] { roleId } }
        });
        return created.GetProperty("customers")[0].GetProperty("id").GetInt32();
    }

    private static int RoleId(JsonElement roles, string systemName) =>
        roles.EnumerateArray().Single(role => role.GetProperty("system_name").GetString() == systemName).GetProperty("id").GetInt32();

    private static IEnumerable<int> CustomerRoleIds(JsonElement customer) =>
        customer.GetProperty("customers")[0].GetProperty("role_ids").EnumerateArray().Select(role => role.GetInt32());

    private Task AuthenticateAdministrator() => Authenticate(new { username = _username, password = _password });

    private async Task Authenticate(object credentials)
    {
        var token = await Send(HttpMethod.Post, "/token", HttpStatusCode.OK, credentials);
        _customerId = token.GetProperty("customer_id").GetInt32();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("access_token").GetString());
    }

    private async Task<JsonElement> Send(HttpMethod method, string path, HttpStatusCode status, object body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        using var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        // Authentication responses can contain a bearer token. Keep their bodies out of assertion output.
        TestAssert.That(response.StatusCode, Is.EqualTo(status),
            path == "/token" ? $"{method} {path}" : $"{method} {path}\n{content}");
        if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true ||
            string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }
}
