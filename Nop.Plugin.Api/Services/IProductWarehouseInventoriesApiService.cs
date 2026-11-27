using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Api.Infrastructure;

namespace Nop.Plugin.Api.Services;

public interface IProductWarehouseInventoriesApiService
{
    Task<IList<ProductWarehouseInventory>> GetMappingsAsync(
        Customer actor, int storeId, int? productId = null,
        int? warehouseId = null, int limit = Constants.Configurations.DefaultLimit,
        int page = Constants.Configurations.DefaultPageValue, int sinceId = Constants.Configurations.DefaultSinceId);

    Task<int> GetIvnentoriesCountAsync(int? productId = null, int? warehouseId = null);

    Task<ProductWarehouseInventory> GetByIdAsync(int id);

    Task<ProductWarehouseInventory> GetScopedByIdAsync(int id, Customer actor, int storeId);
}