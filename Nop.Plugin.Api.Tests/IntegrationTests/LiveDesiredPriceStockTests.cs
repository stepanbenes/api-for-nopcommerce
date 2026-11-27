using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NUnit.Framework;
using TestAssert = NUnit.Framework.Assert;

namespace Nop.Plugin.Api.Tests.IntegrationTests;

public partial class LiveApiTests
{
    private const string CapabilitiesPath = "/api/sync/capabilities";
    private const string DesiredPriceStockPath = "/api/products/desired-price-stock";

    [Test, Category("DatabaseIntegration")]
    public async Task FreshSyncCapabilitiesRequireNoReceiptSchema()
    {
        var database = PostgresProbe.ForConfiguredContainer(_client.BaseAddress);
        await AuthenticateAdministrator();
        var capabilities = await Send(HttpMethod.Get, CapabilitiesPath, HttpStatusCode.OK);
        TestAssert.That(capabilities.GetProperty("desired_price_stock").GetBoolean(), Is.True);
        TestAssert.That(capabilities.GetProperty("warehouse_stock_compare_and_set").GetBoolean(), Is.True);

        var schema = await database.ReadAsync("""
            SELECT jsonb_build_object(
                'has_table', to_regclass('"BulkPriceStockReceipt"') IS NOT NULL,
                'has_marker', EXISTS (SELECT 1 FROM "MigrationVersionInfo"
                    WHERE "Description" = 'API bulk price and stock receipts'))
            """, 0);
        if (Environment.GetEnvironmentVariable("NOP_API_TEST_EXPECT_NO_RECEIPTS") == "1")
        {
            TestAssert.That(schema.GetProperty("has_table").GetBoolean(), Is.False);
            TestAssert.That(schema.GetProperty("has_marker").GetBoolean(), Is.False);
        }

        if (!schema.GetProperty("has_table").GetBoolean())
            TestAssert.That(capabilities.GetProperty("versioned_bulk_price_stock").GetBoolean(), Is.False);

        if (!capabilities.GetProperty("versioned_bulk_price_stock").GetBoolean())
            await Send(HttpMethod.Post, BulkPriceStockPath, HttpStatusCode.ServiceUnavailable,
                BulkRequest(Guid.NewGuid(), 1, BulkItem(int.MaxValue, 21m, 8)));
    }

    [Test, Category("DatabaseIntegration")]
    public async Task DesiredPriceStockRetriesAnUnreadResultAndRejectsStaleValues()
    {
        var database = PostgresProbe.ForConfiguredContainer(_client.BaseAddress);
        await AuthenticateAdministrator();
        var product = await CreateBulkProduct(lowStockActivity: 2);
        try
        {
            var first = DesiredRequest(DesiredItem(product, 12m, 21m, 3, 8));
            using (var request = new HttpRequestMessage(HttpMethod.Post, DesiredPriceStockPath) { Content = JsonContent.Create(first) })
            using (var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                TestAssert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var firstTimestamp = await ReadBulkTimestamp(database, product);
            AssertBulkResults(await Send(HttpMethod.Post, DesiredPriceStockPath, HttpStatusCode.OK, first),
                (product, "unchanged"));
            TestAssert.That(await ReadBulkTimestamp(database, product), Is.EqualTo(firstTimestamp));

            AssertBulkResults(await Send(HttpMethod.Post, DesiredPriceStockPath, HttpStatusCode.OK,
                DesiredRequest(DesiredItem(product, 21m, 22m, 8, 9))), (product, "updated"));
            AssertBulkResults(await Send(HttpMethod.Post, DesiredPriceStockPath, HttpStatusCode.OK, first),
                (product, "conflict"));
            await AssertBulkState(database, product, 22m, 9, (5, 8), (1, 9));
            var zero = DesiredRequest(new { product_id = product, expected_stock_quantity = 9, stock_quantity = 0 });
            AssertBulkResults(await Send(HttpMethod.Post, DesiredPriceStockPath, HttpStatusCode.OK, zero),
                (product, "updated"));
            var zeroTimestamp = await ReadBulkTimestamp(database, product);
            AssertBulkResults(await Send(HttpMethod.Post, DesiredPriceStockPath, HttpStatusCode.OK, zero),
                (product, "unchanged"));
            TestAssert.That(await ReadBulkTimestamp(database, product), Is.EqualTo(zeroTimestamp));

            AssertBulkResults(await Send(HttpMethod.Post, DesiredPriceStockPath, HttpStatusCode.OK,
                DesiredRequest(new { product_id = product, expected_price = 22m, price = 23m })),
                (product, "updated"));
            TestAssert.That(await ReadBulkPublication(database, product), Is.False);
            await AssertBulkState(database, product, 23m, 0, (5, 8), (1, 9), (-9, 0));
        }
        finally
        {
            await DeleteBulkProducts(product);
        }
    }

    [Test, Category("DatabaseIntegration")]
    public async Task DesiredPriceStockValidatesTheEntireBatchBeforeWriting()
    {
        var database = PostgresProbe.ForConfiguredContainer(_client.BaseAddress);
        await AuthenticateAdministrator();
        var product = await CreateBulkProduct();
        var unsupported = 0;
        try
        {
            unsupported = await CreateBulkProduct(inventoryMode: 2);
            var good = DesiredItem(product, 12m, 21m, 3, 8);
            await Send(HttpMethod.Post, DesiredPriceStockPath, HttpStatusCode.BadRequest,
                DesiredRequest(good, new { product_id = unsupported, price = 21m }));
            await Send(HttpMethod.Post, DesiredPriceStockPath, HttpStatusCode.BadRequest, DesiredRequest(good, good));
            await AssertBulkState(database, product, 12m, 3);

            AssertBulkResults(await Send(HttpMethod.Post, DesiredPriceStockPath, HttpStatusCode.OK, DesiredRequest(
                DesiredItem(int.MaxValue, 12m, 21m, 3, 8), good, DesiredItem(unsupported, 12m, 21m, 3, 8))),
                (int.MaxValue, "not_found"), (product, "updated"), (unsupported, "unsupported_stock_mode"));
            AssertBulkResults(await Send(HttpMethod.Post, DesiredPriceStockPath, HttpStatusCode.OK,
                DesiredRequest(new { product_id = unsupported, expected_price = 12m, price = 21m })),
                (unsupported, "updated"));
            await AssertBulkState(database, unsupported, 21m, 3);
        }
        finally
        {
            await DeleteBulkProducts(unsupported, product);
        }
    }

    [Test, Category("DatabaseIntegration")]
    public async Task ConcurrentDesiredStockRetriesWriteOneHistoryEntry()
    {
        var database = PostgresProbe.ForConfiguredContainer(_client.BaseAddress);
        await AuthenticateAdministrator();
        var product = await CreateBulkProduct();
        try
        {
            var request = DesiredRequest(DesiredItem(product, 12m, 21m, 3, 8));
            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
                Send(HttpMethod.Post, DesiredPriceStockPath, HttpStatusCode.OK, request)));
            var statuses = results.Select(result => result.GetProperty("results")[0].GetProperty("status").GetString());
            TestAssert.That(statuses, Is.EquivalentTo(new[] { "updated", "unchanged", "unchanged", "unchanged" }));
            await AssertBulkState(database, product, 21m, 8, (5, 8));
        }
        finally
        {
            await DeleteBulkProducts(product);
        }
    }

    [Test, Category("DatabaseIntegration")]
    public async Task WarehouseDesiredStockPreservesReservationsAndWritesHistoryOnce()
    {
        var database = PostgresProbe.ForConfiguredContainer(_client.BaseAddress);
        await AuthenticateAdministrator();
        var warehouseId = 0;
        var productId = 0;
        var inventoryId = 0;
        const string inventoryPath = "/api/product_warehouse_inventories";
        try
        {
            var warehouse = await Send(HttpMethod.Post, "/api/warehouses", HttpStatusCode.OK,
                new { warehouse = new { name = $"API CAS test {Guid.NewGuid():N}", address = new { city = "Test" } } });
            warehouseId = warehouse.GetProperty("warehouses")[0].GetProperty("id").GetInt32();
            productId = await CreateBulkProduct(useMultipleWarehouses: true, lowStockActivity: 2);
            var created = await Send(HttpMethod.Post, inventoryPath, HttpStatusCode.OK, new
            {
                product_warehouse_inventory = new { product_id = productId, warehouse_id = warehouseId, stock_quantity = 6, reserved_quantity = 3 }
            });
            inventoryId = created.GetProperty("product_warehouse_inventories")[0].GetProperty("id").GetInt32();
            var path = $"{inventoryPath}/{inventoryId}";
            var request = new { product_warehouse_inventory = new { expected_stock_quantity = 6, stock_quantity = 8 } };
            await Send(HttpMethod.Put, path, HttpStatusCode.OK, request);
            var replay = await Send(HttpMethod.Put, path, HttpStatusCode.OK, request);
            var row = replay.GetProperty("product_warehouse_inventories")[0];
            TestAssert.That(row.GetProperty("stock_quantity").GetInt32(), Is.EqualTo(8));
            TestAssert.That(row.GetProperty("reserved_quantity").GetInt32(), Is.EqualTo(3));

            await Send(HttpMethod.Put, path, HttpStatusCode.Conflict,
                new { product_warehouse_inventory = new { expected_stock_quantity = 6, stock_quantity = 9 } });
            await Send(HttpMethod.Put, path, HttpStatusCode.UnprocessableEntity,
                new { product_warehouse_inventory = new { expected_stock_quantity = 8, stock_quantity = 2 } });

            await Send(HttpMethod.Put, path, HttpStatusCode.OK,
                new { product_warehouse_inventory = new { expected_stock_quantity = 8, stock_quantity = 3 } });
            TestAssert.That(await ReadBulkPublication(database, productId), Is.False);

            var entries = await database.ReadAsync("""
                SELECT COALESCE(jsonb_agg(jsonb_build_array("WarehouseId", "QuantityAdjustment", "StockQuantity") ORDER BY "Id"), '[]'::jsonb)
                FROM "StockQuantityHistory" WHERE "ProductId" = :'id'::integer AND "Message" = 'API desired stock update'
                """, productId);
            TestAssert.That(entries.GetArrayLength(), Is.EqualTo(2));
            TestAssert.That(entries[0].EnumerateArray().Select(value => value.GetInt32()), Is.EqualTo(new[] { warehouseId, 2, 8 }));
            TestAssert.That(entries[1].EnumerateArray().Select(value => value.GetInt32()), Is.EqualTo(new[] { warehouseId, -5, 3 }));
        }
        finally
        {
            if (inventoryId > 0)
                await Send(HttpMethod.Delete, $"{inventoryPath}/{inventoryId}", HttpStatusCode.OK);
            await DeleteBulkProducts(productId);
            if (warehouseId > 0)
                await Send(HttpMethod.Delete, $"/api/warehouses/{warehouseId}", HttpStatusCode.OK);
        }
    }

    private static IEnumerable<TestCaseData> WarehouseReadbackScopeCases()
    {
        yield return new TestCaseData(false).SetName("WarehouseAndProductReadbackExcludeSoftDeletedOwnerBeforePaging");
        if (Environment.GetEnvironmentVariable("NOP_API_TEST_CATALOG_ACL") == "1")
            yield return new TestCaseData(true).SetName("WarehouseAndProductReadbackExcludeAclRestrictedOwnerBeforePaging");
    }

    [TestCaseSource(nameof(WarehouseReadbackScopeCases))]
    public async Task WarehouseAndProductReadbackExcludeOutOfScopeProductsBeforePaging(bool enforceAcl)
    {
        await AuthenticateAdministrator();
        var roles = (await Send(HttpMethod.Get, "/api/customer_roles", HttpStatusCode.OK)).GetProperty("customer_roles");
        var guestRole = RoleId(roles, "Guests");
        if (enforceAcl)
            await Send(HttpMethod.Post, "/api/admin/cache/clear", HttpStatusCode.NoContent);
        const string inventoryPath = "/api/product_warehouse_inventories";
        var warehouseId = 0;
        var hiddenProductId = 0;
        var visibleProductId = 0;
        var hiddenInventoryId = 0;
        var visibleInventoryId = 0;
        var hiddenProductDeleted = false;
        try
        {
            var warehouse = await Send(HttpMethod.Post, "/api/warehouses", HttpStatusCode.OK,
                new { warehouse = new { name = $"API scope test {Guid.NewGuid():N}", address = new { city = "Test" } } });
            warehouseId = warehouse.GetProperty("warehouses")[0].GetProperty("id").GetInt32();

            var hidden = await Send(HttpMethod.Post, "/api/products", HttpStatusCode.OK,
                new { product = new { name = $"API scope hidden {Guid.NewGuid():N}", role_ids = new[] { guestRole },
                    manage_inventory_method_id = 1, use_multiple_warehouses = true } });
            hiddenProductId = hidden.GetProperty("products")[0].GetProperty("id").GetInt32();
            visibleProductId = await CreateBulkProduct(useMultipleWarehouses: true);

            hiddenInventoryId = await CreateInventory(hiddenProductId);
            visibleInventoryId = await CreateInventory(visibleProductId);
            if (!enforceAcl)
            {
                await DeleteBulkProducts(hiddenProductId);
                hiddenProductDeleted = true;
            }

            await Send(HttpMethod.Get, $"/api/products/{hiddenProductId}", HttpStatusCode.NotFound);
            await Send(HttpMethod.Put, $"/api/products/{hiddenProductId}", HttpStatusCode.NotFound,
                new { product = new { price = 20 } });
            await Send(HttpMethod.Get, $"/api/products/{visibleProductId}", HttpStatusCode.OK);
            await Send(HttpMethod.Get, $"{inventoryPath}/{hiddenInventoryId}", HttpStatusCode.NotFound);
            await Send(HttpMethod.Get, $"{inventoryPath}/{visibleInventoryId}", HttpStatusCode.OK);
            var hiddenList = await Send(HttpMethod.Get, $"{inventoryPath}?ProductId={hiddenProductId}", HttpStatusCode.OK);
            TestAssert.That(hiddenList.GetProperty("product_warehouse_inventories").GetArrayLength(), Is.Zero);

            var page = await Send(HttpMethod.Get, $"{inventoryPath}?WarehouseId={warehouseId}&Limit=1", HttpStatusCode.OK);
            var rows = page.GetProperty("product_warehouse_inventories");
            TestAssert.That(rows.GetArrayLength(), Is.EqualTo(1));
            TestAssert.That(rows[0].GetProperty("id").GetInt32(), Is.EqualTo(visibleInventoryId));

            var update = await Send(HttpMethod.Post, DesiredPriceStockPath, HttpStatusCode.OK,
                DesiredRequest(new { product_id = hiddenProductId, expected_price = 0, price = 3 }));
            AssertBulkResults(update, (hiddenProductId, "not_found"));
        }
        finally
        {
            foreach (var id in new[] { hiddenInventoryId, visibleInventoryId }.Where(id => id > 0))
                await Send(HttpMethod.Delete, $"{inventoryPath}/{id}", HttpStatusCode.OK);
            await DeleteBulkProducts(hiddenProductDeleted ? 0 : hiddenProductId, visibleProductId);
            if (warehouseId > 0)
                await Send(HttpMethod.Delete, $"/api/warehouses/{warehouseId}", HttpStatusCode.OK);
        }

        async Task<int> CreateInventory(int productId)
        {
            var created = await Send(HttpMethod.Post, inventoryPath, HttpStatusCode.OK, new
            {
                product_warehouse_inventory = new { product_id = productId, warehouse_id = warehouseId, stock_quantity = 7 }
            });
            return created.GetProperty("product_warehouse_inventories")[0].GetProperty("id").GetInt32();
        }
    }

    private async Task RequireExistingReceiptSchema()
    {
        var capabilities = await Send(HttpMethod.Get, CapabilitiesPath, HttpStatusCode.OK);
        if (!capabilities.GetProperty("versioned_bulk_price_stock").GetBoolean())
            TestAssert.Ignore("Legacy replay tests require a store that already has the receipt table and unique index.");
    }

    private static async Task<string> ReadBulkTimestamp(PostgresProbe database, int productId) =>
        (await database.ReadAsync("""SELECT to_jsonb("UpdatedOnUtc") FROM "Product" WHERE "Id" = :'id'::integer""", productId)).GetString();

    private static async Task<bool> ReadBulkPublication(PostgresProbe database, int productId) =>
        (await database.ReadAsync("""SELECT to_jsonb("Published") FROM "Product" WHERE "Id" = :'id'::integer""", productId)).GetBoolean();

    private static object DesiredRequest(params object[] items) => new { items };

    private static object DesiredItem(int productId, decimal expectedPrice, decimal price, int expectedStock, int stock) => new
    {
        product_id = productId,
        expected_price = expectedPrice,
        price,
        expected_stock_quantity = expectedStock,
        stock_quantity = stock
    };
}
