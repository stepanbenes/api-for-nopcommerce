using LinqToDB.Async;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Events;
using Nop.Data;
using Nop.Plugin.Api.DTO.Products;
using Nop.Services.Catalog;
using Nop.Services.Catalog.Caching;

namespace Nop.Plugin.Api.Services;

public sealed class DesiredPriceStockService(
    INopDataProvider dataProvider,
    IRepository<ProductWarehouseInventory> inventoryRepository,
    IProductService productService,
    CatalogSettings catalogSettings,
    ProductSyncScope productScope)
{
    private const string HistoryMessage = "API desired stock update";

    public Task<BulkPriceStockResponse> ExecuteAsync(Customer actor, int storeId, DesiredPriceStockRequest request,
        CancellationToken cancellationToken) => ExecuteTransactionAsync(async publish =>
    {
        var ids = request.Items.Select(item => item.ProductId).ToArray();
        var scoped = await productScope.GetProductsAsync(actor, storeId);
        var products = (await scoped.Where(product => ids.Contains(product.Id)).ToListAsync(cancellationToken))
            .ToDictionary(product => product.Id);

        var statuses = new Dictionary<int, string>();
        foreach (var item in request.Items.OrderBy(item => item.ProductId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            statuses[item.ProductId] = products.TryGetValue(item.ProductId, out var product)
                ? await ApplyProductAsync(product, item, publish)
                : "not_found";
        }

        return new BulkPriceStockResponse(request.Items
            .Select(item => new BulkPriceStockResult(item.ProductId, statuses[item.ProductId])).ToList());
    }, cancellationToken);

    public Task<(string Status, ProductWarehouseInventory Inventory)> UpdateWarehouseAsync(Customer actor, int storeId,
        int inventoryId, int expectedStock, int desiredStock, CancellationToken cancellationToken) =>
        ExecuteTransactionAsync<(string, ProductWarehouseInventory)>(async publish =>
        {
            var inventory = await inventoryRepository.Table.FirstOrDefaultAsync(row => row.Id == inventoryId, cancellationToken);
            if (inventory == null)
                return ("not_found", null);

            var scoped = await productScope.GetProductsAsync(actor, storeId);
            var product = await scoped.FirstOrDefaultAsync(row => row.Id == inventory.ProductId, cancellationToken);
            if (product == null)
                return ("not_found", null);
            if (product.ManageInventoryMethod != ManageInventoryMethod.ManageStock || !product.UseMultipleWarehouses)
                return ("unsupported_stock_mode", null);
            if (inventory.StockQuantity == desiredStock)
                return ("unchanged", inventory);
            if (inventory.StockQuantity != expectedStock)
                return ("conflict", null);
            if (desiredStock < inventory.ReservedQuantity)
                return ("below_reserved_quantity", null);

            var adjustment = (long)desiredStock - inventory.StockQuantity;
            if (adjustment is < int.MinValue or > int.MaxValue)
                return ("stock_adjustment_out_of_range", null);

            // The serializable read preserves the reservation and linkage values of this native row update.
            inventory.StockQuantity = desiredStock;
            await productService.UpdateProductWarehouseInventoryAsync(inventory);
            await productService.AddStockQuantityHistoryEntryAsync(product, (int)adjustment, desiredStock,
                inventory.WarehouseId, HistoryMessage);
            publish.Add(() => new ProductWarehouseInventoryCacheEventConsumer()
                .HandleEventAsync(new EntityUpdatedEvent<ProductWarehouseInventory>(inventory)));

            if (product.LowStockActivity != LowStockActivity.Nothing)
                await ApplyWarehouseLowStockActivityAsync(product, publish, cancellationToken);

            return ("updated", inventory);
        }, cancellationToken);

    private async Task<string> ApplyProductAsync(Product product, DesiredPriceStockItem item, List<Func<Task>> publish)
    {
        if (item.StockQuantity.HasValue &&
            (product.ManageInventoryMethod != ManageInventoryMethod.ManageStock || product.UseMultipleWarehouses))
            return "unsupported_stock_mode";

        var stockQuantity = item.StockQuantity ?? product.StockQuantity;
        var price = item.Price ?? product.Price;
        var adjustment = (long)stockQuantity - product.StockQuantity;
        if (adjustment == 0 && price == product.Price)
            return "unchanged";

        if (item.ExpectedPrice is { } expectedPrice && expectedPrice != product.Price ||
            item.ExpectedStockQuantity is { } expectedStock && expectedStock != product.StockQuantity)
            return "conflict";

        if (adjustment is < int.MinValue or > int.MaxValue)
            return "stock_adjustment_out_of_range";

        // The serializable read protects the expected values used by nopCommerce's native catalog writes.
        product.Price = price;
        product.UpdatedOnUtc = DateTime.UtcNow;
        if (adjustment != 0)
            await productService.AdjustInventoryAsync(product, (int)adjustment, message: HistoryMessage);
        else
            await productService.UpdateProductAsync(product);

        RecheckProductCacheAfterCommit(product, publish);
        return "updated";
    }

    private async Task ApplyWarehouseLowStockActivityAsync(Product product, List<Func<Task>> publish,
        CancellationToken cancellationToken)
    {
        // The available total subtracts reservations across all warehouse rows, like nopCommerce inventory.
        var available = await inventoryRepository.Table.Where(row => row.ProductId == product.Id)
            .SumAsync(row => (long)row.StockQuantity - row.ReservedQuantity, cancellationToken);
        var atMinimum = available <= product.MinStockQuantity;
        if (!atMinimum && !catalogSettings.PublishBackProductWhenCancellingOrders)
            return;

        if (product.LowStockActivity == LowStockActivity.Unpublish && product.Published == atMinimum)
        {
            product.Published = !atMinimum;
        }
        else if (product.LowStockActivity == LowStockActivity.DisableBuyButton &&
            (product.DisableBuyButton != atMinimum || product.DisableWishlistButton != atMinimum))
        {
            product.DisableBuyButton = atMinimum;
            product.DisableWishlistButton = atMinimum;
        }
        else
        {
            return;
        }

        await productService.UpdateProductAsync(product);
        RecheckProductCacheAfterCommit(product, publish);
    }

    private static void RecheckProductCacheAfterCommit(Product product, List<Func<Task>> publish)
    {
        // A storefront read can refill the cache between the native event and transaction commit.
        publish.Add(() => new ProductCacheEventConsumer().HandleEventAsync(new EntityUpdatedEvent<Product>(product)));
    }

    private async Task<T> ExecuteTransactionAsync<T>(Func<List<Func<Task>>, Task<T>> apply, CancellationToken cancellationToken)
    {
        BulkPriceStockService.EnsureTransactionalSettings();
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var publish = new List<Func<Task>>();
            T result;
            try
            {
                using var transaction = dataProvider.CreateTransactionScope();
                result = await apply(publish);
                cancellationToken.ThrowIfCancellationRequested();
                transaction.Complete();
            }
            catch (Exception exception) when (BulkPriceStockService.IsConcurrentWrite(exception))
            {
                if (attempt == 3)
                    throw new BulkPriceStockBusyException("A concurrent write prevented the desired state update. Retry the same request.", exception);

                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
                continue;
            }

            foreach (var notify in publish)
                await notify();

            return result;
        }
    }
}
