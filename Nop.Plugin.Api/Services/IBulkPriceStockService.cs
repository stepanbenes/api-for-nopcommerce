using Nop.Core.Domain.Customers;
using Nop.Plugin.Api.DTO.Products;

namespace Nop.Plugin.Api.Services;

public interface IBulkPriceStockService
{
    Task<BulkPriceStockResponse> ExecuteAsync(Customer actor, int storeId, BulkPriceStockRequest request,
        CancellationToken cancellationToken);
}

public sealed class BulkPriceStockConflictException(string message) : Exception(message);

public sealed class BulkPriceStockBusyException(string message, Exception innerException = null)
    : Exception(message, innerException);
