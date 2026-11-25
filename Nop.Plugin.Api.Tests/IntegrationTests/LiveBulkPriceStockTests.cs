using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NUnit.Framework;
using TestAssert = NUnit.Framework.Assert;

namespace Nop.Plugin.Api.Tests.IntegrationTests;

public partial class LiveApiTests
{
    private const string BulkPriceStockPath = "/api/products/bulk-price-stock";

    [Test, Category("DatabaseIntegration")]
    public async Task BulkPriceStockReplaysAnUnreadResponseAfterANewerUpdate()
    {
        var database = PostgresProbe.ForConfiguredContainer(_client.BaseAddress);
        await AuthenticateAdministrator();
        var product = await CreateBulkProduct();
        try
        {
            var first = BulkRequest(Guid.NewGuid(), 10, BulkItem(product, 21m, 8));
            using (var request = new HttpRequestMessage(HttpMethod.Post, BulkPriceStockPath) { Content = JsonContent.Create(first) })
            using (var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                TestAssert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            await AssertBulkState(database, product, 21m, 8, (5, 8));
            await Send(HttpMethod.Post, BulkPriceStockPath, HttpStatusCode.OK,
                BulkRequest(first.operation_id, 11, BulkItem(product, 22m, 9)));
            var replay = await Send(HttpMethod.Post, BulkPriceStockPath, HttpStatusCode.OK, first);
            AssertBulkResults(replay, (product, "updated"));

            await Send(HttpMethod.Post, BulkPriceStockPath, HttpStatusCode.Conflict,
                BulkRequest(first.operation_id, 9, BulkItem(product, 31m, 15)));
            await Send(HttpMethod.Post, BulkPriceStockPath, HttpStatusCode.Conflict,
                BulkRequest(first.operation_id, 10, BulkItem(product, 32m, 16)));
            await AssertBulkState(database, product, 22m, 9, (5, 8), (1, 9));
        }
        finally
        {
            await DeleteBulkProducts(product);
        }
    }

    [Test, Category("DatabaseIntegration")]
    public async Task BulkPriceStockValidatesTheBatchBeforeWritingAndReportsUnavailableItems()
    {
        var database = PostgresProbe.ForConfiguredContainer(_client.BaseAddress);
        await AuthenticateAdministrator();
        var product = await CreateBulkProduct();
        var unsupported = 0;
        try
        {
            unsupported = await CreateBulkProduct(inventoryMode: 2);
            var operationId = Guid.NewGuid();
            await Send(HttpMethod.Post, BulkPriceStockPath, HttpStatusCode.BadRequest,
                BulkRequest(operationId, 1, BulkItem(product, 21m, 8), BulkItem(unsupported, -1m, 7)));
            await Send(HttpMethod.Post, BulkPriceStockPath, HttpStatusCode.BadRequest,
                new { operation_id = operationId, version = 1.5m, items = new[] { BulkItem(product, 21m, 8) } });
            await AssertBulkState(database, product, 12m, 3);

            var valid = await Send(HttpMethod.Post, BulkPriceStockPath, HttpStatusCode.OK,
                BulkRequest(operationId, 1, BulkItem(product, 21m, 8),
                    BulkItem(unsupported, 55m, 10), BulkItem(int.MaxValue, 91m, 90)));
            AssertBulkResults(valid, (product, "updated"), (unsupported, "unsupported_stock_mode"),
                (int.MaxValue, "not_found"));
            await AssertBulkState(database, product, 21m, 8, (5, 8));
            await AssertBulkState(database, unsupported, 12m, 3);

            var reordered = await Send(HttpMethod.Post, BulkPriceStockPath, HttpStatusCode.OK,
                BulkRequest(operationId, 1, BulkItem(int.MaxValue, 91m, 90),
                    BulkItem(unsupported, 55m, 10), BulkItem(product, 21m, 8)));
            AssertBulkResults(reordered, (product, "updated"), (unsupported, "unsupported_stock_mode"),
                (int.MaxValue, "not_found"));

            var priceOnly = await Send(HttpMethod.Post, BulkPriceStockPath, HttpStatusCode.OK,
                BulkRequest(operationId, 2, new { product_id = unsupported, price = 55m }));
            AssertBulkResults(priceOnly, (unsupported, "updated"));
            await AssertBulkState(database, unsupported, 55m, 3);
        }
        finally
        {
            await DeleteBulkProducts(unsupported, product);
        }
    }

    [Test, Category("DatabaseIntegration")]
    public async Task ConcurrentBulkPriceStockRetriesWriteOneHistoryEntry()
    {
        var database = PostgresProbe.ForConfiguredContainer(_client.BaseAddress);
        await AuthenticateAdministrator();
        var product = await CreateBulkProduct();
        try
        {
            var request = BulkRequest(Guid.NewGuid(), 1, BulkItem(product, 21m, 8));
            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
                Send(HttpMethod.Post, BulkPriceStockPath, HttpStatusCode.OK, request)));
            foreach (var result in results)
                AssertBulkResults(result, (product, "updated"));
            await AssertBulkState(database, product, 21m, 8, (5, 8));
        }
        finally
        {
            await DeleteBulkProducts(product);
        }
    }

    [Test, Category("DatabaseIntegration")]
    public async Task ConcurrentBulkPriceStockVersionsLeaveTheNewerUpdateInPlace()
    {
        var database = PostgresProbe.ForConfiguredContainer(_client.BaseAddress);
        await AuthenticateAdministrator();
        var product = await CreateBulkProduct();
        try
        {
            var operationId = Guid.NewGuid();
            var older = BulkRequest(operationId, 1, BulkItem(product, 21m, 8));
            var newer = BulkRequest(operationId, 2, BulkItem(product, 22m, 9));

            var newerAttempt = Send(HttpMethod.Post, BulkPriceStockPath, HttpStatusCode.OK, newer);
            var olderAttempt = _client.PostAsJsonAsync(BulkPriceStockPath, older);
            await Task.WhenAll(newerAttempt, olderAttempt);
            using var olderResponse = await olderAttempt;
            TestAssert.That(olderResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK).Or.EqualTo(HttpStatusCode.Conflict));
            AssertBulkResults(await newerAttempt, (product, "updated"));

            var olderReplay = await Send(HttpMethod.Post, BulkPriceStockPath, olderResponse.StatusCode, older);
            if (olderResponse.StatusCode == HttpStatusCode.OK)
                AssertBulkResults(olderReplay, (product, "updated"));
            var newerReplay = await Send(HttpMethod.Post, BulkPriceStockPath, HttpStatusCode.OK, newer);
            AssertBulkResults(newerReplay, (product, "updated"));
            await Send(HttpMethod.Post, BulkPriceStockPath, HttpStatusCode.Conflict,
                BulkRequest(operationId, 2, BulkItem(product, 31m, 15)));

            var expectedHistory = olderResponse.StatusCode == HttpStatusCode.OK
                ? new[] { (5, 8), (1, 9) }
                : new[] { (6, 9) };
            await AssertBulkState(database, product, 22m, 9, expectedHistory);
        }
        finally
        {
            await DeleteBulkProducts(product);
        }
    }

    [Test, Category("DatabaseIntegration")]
    public async Task BulkPriceStockRollsBackProductAndHistoryWhenReceiptWriteFails()
    {
        var database = PostgresProbe.ForConfiguredContainer(_client.BaseAddress);
        PostgresProbe.RequireDisposableStoreForFaultInjection();
        await AuthenticateAdministrator();
        var firstProduct = await CreateBulkProduct();
        var secondProduct = 0;
        try
        {
            secondProduct = await CreateBulkProduct();
            var operationId = Guid.NewGuid();
            var request = BulkRequest(operationId, 1, BulkItem(firstProduct, 21m, 8), BulkItem(secondProduct, 22m, 9));
            var faultName = $"bulk_test_{operationId:N}";
            await database.ExecuteInDisposableStoreAsync($"""
                BEGIN;
                CREATE FUNCTION "{faultName}"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Injected bulk operation commit failure';
                END;
                $$;
                CREATE CONSTRAINT TRIGGER "{faultName}"
                    AFTER INSERT ON "BulkPriceStockReceipt"
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
                    WHEN (NEW."OperationId" = '{operationId:D}')
                    EXECUTE FUNCTION "{faultName}"();
                COMMIT;
                """);
            try
            {
                using var response = await _client.PostAsJsonAsync(BulkPriceStockPath, request);
                TestAssert.That((int)response.StatusCode, Is.InRange(500, 599));
                await AssertBulkState(database, firstProduct, 12m, 3);
                await AssertBulkState(database, secondProduct, 12m, 3);
            }
            finally
            {
                await database.ExecuteInDisposableStoreAsync($"""
                    BEGIN;
                    DROP TRIGGER "{faultName}" ON "BulkPriceStockReceipt";
                    DROP FUNCTION "{faultName}"();
                    COMMIT;
                    """);
            }

            var retried = await Send(HttpMethod.Post, BulkPriceStockPath, HttpStatusCode.OK, request);
            AssertBulkResults(retried, (firstProduct, "updated"), (secondProduct, "updated"));
            await AssertBulkState(database, firstProduct, 21m, 8, (5, 8));
            await AssertBulkState(database, secondProduct, 22m, 9, (6, 9));
        }
        finally
        {
            await DeleteBulkProducts(secondProduct, firstProduct);
        }
    }

    private sealed record BulkTestRequest(Guid operation_id, long version, object[] items);

    private static BulkTestRequest BulkRequest(Guid operationId, long version, params object[] items) =>
        new(operationId, version, items);

    private static object BulkItem(int id, decimal price, int stock) =>
        new { product_id = id, price, stock_quantity = stock };

    private async Task<int> CreateBulkProduct(int inventoryMode = 1)
    {
        var created = await Send(HttpMethod.Post, "/api/products", HttpStatusCode.OK, new
        {
            product = new
            {
                name = $"API integration bulk {Guid.NewGuid():N}",
                price = 12m,
                stock_quantity = 3,
                manage_inventory_method_id = inventoryMode,
                published = true
            }
        });
        return created.GetProperty("products")[0].GetProperty("id").GetInt32();
    }

    private async Task DeleteBulkProducts(params int[] productIds)
    {
        foreach (var productId in productIds.Where(id => id != 0))
            await Send(HttpMethod.Delete, $"/api/products/{productId}", HttpStatusCode.OK);
    }

    private static void AssertBulkResults(JsonElement response, params (int ProductId, string Status)[] expected)
    {
        var results = response.GetProperty("results").EnumerateArray().Select(item =>
            (item.GetProperty("product_id").GetInt32(), item.GetProperty("status").GetString()));
        TestAssert.That(results, Is.EqualTo(expected));
    }

    private static async Task AssertBulkState(PostgresProbe database, int product, decimal price, int stock,
        params (int Adjustment, int Quantity)[] history)
    {
        const string productQuery = """
            SELECT jsonb_build_object('price', p."Price", 'stock', p."StockQuantity", 'history', (
                SELECT COALESCE(jsonb_agg(jsonb_build_array(h."QuantityAdjustment", h."StockQuantity") ORDER BY h."Id"), '[]'::jsonb)
                FROM "StockQuantityHistory" h WHERE h."ProductId" = p."Id"))
            FROM "Product" p WHERE p."Id" = :'id'::integer
            """;
        var state = await database.ReadAsync(productQuery, product);
        TestAssert.That(state.GetProperty("price").GetDecimal(), Is.EqualTo(price));
        TestAssert.That(state.GetProperty("stock").GetInt32(), Is.EqualTo(stock));
        TestAssert.That(state.GetProperty("history").EnumerateArray().Select(entry =>
            (entry[0].GetInt32(), entry[1].GetInt32())), Is.EqualTo(history));
    }
}
