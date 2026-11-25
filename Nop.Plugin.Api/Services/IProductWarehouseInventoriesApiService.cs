using Nop.Core.Domain.Catalog;
using Nop.Plugin.Api.Infrastructure;

namespace Nop.Plugin.Api.Services;

public interface IProductWarehouseInventoriesApiService
{
    Task<IList<ProductWarehouseInventory>> GetMappingsAsync(
        int? productId = null,
        int? categoryId = null, int limit = Constants.Configurations.DefaultLimit,
        int page = Constants.Configurations.DefaultPageValue, int sinceId = Constants.Configurations.DefaultSinceId);

    Task<int> GetIvnentoriesCountAsync(int? productId = null, int? warehouseId = null);

    Task<ProductWarehouseInventory> GetByIdAsync(int id);
}