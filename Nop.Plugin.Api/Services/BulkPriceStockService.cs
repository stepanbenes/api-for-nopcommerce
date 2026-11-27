using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LinqToDB.Async;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Newtonsoft.Json;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Data;
using Nop.Plugin.Api.Domain;
using Nop.Plugin.Api.DTO.Products;
using Nop.Services.Catalog;
using Npgsql;

namespace Nop.Plugin.Api.Services;

internal sealed class BulkPriceStockService(
    INopDataProvider dataProvider,
    BulkPriceStockAvailability availability,
    IRepository<BulkPriceStockReceipt> receiptRepository,
    IProductService productService,
    ProductSyncScope productScope) : IBulkPriceStockService
{
    public async Task<BulkPriceStockResponse> ExecuteAsync(Customer actor, int storeId, BulkPriceStockRequest request,
        CancellationToken cancellationToken)
    {
        EnsureTransactionalSettings();
        if (!await availability.IsAvailableAsync())
            throw new BulkPriceStockBusyException("This installation does not have the legacy bulk receipt schema.");

        var orderedItems = request.Items.OrderBy(item => item.ProductId).ToArray();
        var canonicalPayload = string.Join('\n', orderedItems.Select(item => FormattableString.Invariant(
            $"{item.ProductId}:{item.Price?.ToString("G29", CultureInfo.InvariantCulture)}:{item.StockQuantity}")));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));

        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await ExecuteTransactionAsync(actor, storeId, request, orderedItems, hash, cancellationToken);
            }
            catch (Exception exception) when (IsConcurrentWrite(exception))
            {
                if (attempt == 3)
                    throw new BulkPriceStockBusyException("A concurrent write prevented this operation from completing. Retry the same request.", exception);

                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
            }
        }
    }

    private async Task<BulkPriceStockResponse> ExecuteTransactionAsync(Customer actor, int storeId,
        BulkPriceStockRequest request, BulkPriceStockItem[] orderedItems, string hash, CancellationToken cancellationToken)
    {
        using var transaction = dataProvider.CreateTransactionScope();
        var operation = receiptRepository.Table.Where(receipt =>
            receipt.ActorId == actor.Id && receipt.StoreId == storeId && receipt.OperationId == request.OperationId);
        var receipt = await operation.FirstOrDefaultAsync(candidate => candidate.Version == request.Version, cancellationToken);
        if (receipt != null)
        {
            if (!string.Equals(receipt.RequestHash, hash, StringComparison.Ordinal))
                throw new BulkPriceStockConflictException("This operation ID and version already belong to a different payload.");

            var replay = JsonConvert.DeserializeObject<BulkPriceStockResponse>(receipt.ResponseJson);
            transaction.Complete();
            return replay;
        }

        if (await operation.AnyAsync(candidate => candidate.Version > request.Version, cancellationToken))
            throw new BulkPriceStockConflictException("A higher version of this operation already completed.");

        // Claim the key before invoking product observers. The receipt becomes visible only at commit.
        cancellationToken.ThrowIfCancellationRequested();
        receipt = new BulkPriceStockReceipt
        {
            ActorId = actor.Id,
            StoreId = storeId,
            OperationId = request.OperationId,
            Version = request.Version,
            RequestHash = hash
        };
        await receiptRepository.InsertAsync(receipt, publishEvent: false);

        var productIds = orderedItems.Select(item => item.ProductId).ToArray();
        var query = await productScope.GetProductsAsync(actor, storeId);
        var products = (await query.Where(product => productIds.Contains(product.Id)).ToListAsync(cancellationToken))
            .ToDictionary(product => product.Id);

        var statuses = new Dictionary<int, string>();
        foreach (var item in orderedItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            statuses[item.ProductId] = products.TryGetValue(item.ProductId, out var product)
                ? await ApplyItemAsync(product, item)
                : "not_found";
        }

        var result = new BulkPriceStockResponse(request.Items
            .Select(item => new BulkPriceStockResult(item.ProductId, statuses[item.ProductId])).ToList());
        cancellationToken.ThrowIfCancellationRequested();
        receipt.ResponseJson = JsonConvert.SerializeObject(result);
        await receiptRepository.UpdateAsync(receipt, publishEvent: false);

        transaction.Complete();
        return result;
    }

    private async Task<string> ApplyItemAsync(Product product, BulkPriceStockItem item)
    {
        if (item.StockQuantity.HasValue &&
            (product.ManageInventoryMethod != ManageInventoryMethod.ManageStock || product.UseMultipleWarehouses))
            return "unsupported_stock_mode";

        var stockQuantity = item.StockQuantity ?? product.StockQuantity;
        var adjustment = (long)stockQuantity - product.StockQuantity;
        if (adjustment is < int.MinValue or > int.MaxValue)
            return "stock_adjustment_out_of_range";

        var price = item.Price ?? product.Price;
        if (adjustment == 0 && price == product.Price)
            return "updated";

        product.Price = price;
        product.UpdatedOnUtc = DateTime.UtcNow;
        if (adjustment != 0)
            await productService.AdjustInventoryAsync(product, (int)adjustment, message: "API bulk stock update");
        else
            await productService.UpdateProductAsync(product);
        return "updated";
    }

    internal static void EnsureTransactionalSettings()
    {
        if (!SupportsTransactionalWrites())
            throw new BulkPriceStockBusyException("Bulk writes require database transaction enlistment and SQL Server NoLock disabled.");
    }

    internal static bool SupportsTransactionalWrites()
    {
        var settings = DataSettingsManager.LoadSettings();
        return settings.DataProvider switch
        {
            DataProviderType.SqlServer => !settings.WithNoLock && new SqlConnectionStringBuilder(settings.ConnectionString).Enlist,
            DataProviderType.MySql => new MySqlConnectionStringBuilder(settings.ConnectionString).AutoEnlist,
            DataProviderType.PostgreSQL => new NpgsqlConnectionStringBuilder(settings.ConnectionString).Enlist,
            _ => false
        };
    }

    internal static bool IsConcurrentWrite(Exception exception)
    {
        for (var cause = exception; cause != null; cause = cause.InnerException)
        {
            if (cause is DbException { SqlState: "40001" or "40P01" } ||
                cause is SqlException { Number: 1205 or 3960 } ||
                cause is MySqlException { Number: 1205 or 1213 } ||
                IsDuplicateReceipt(cause))
                return true;
        }

        return false;
    }

    private static bool IsDuplicateReceipt(Exception exception)
    {
        return exception switch
        {
            PostgresException { SqlState: "23505" } postgres =>
                string.Equals(postgres.ConstraintName, BulkPriceStockReceipt.OperationIndexName, StringComparison.OrdinalIgnoreCase),
            SqlException { Number: 2601 or 2627 } or MySqlException { Number: 1062 } =>
                exception.Message.Contains(BulkPriceStockReceipt.OperationIndexName, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
