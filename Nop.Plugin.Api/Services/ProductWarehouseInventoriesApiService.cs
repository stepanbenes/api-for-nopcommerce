using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Data;
using Nop.Plugin.Api.DataStructures;
using Nop.Plugin.Api.Infrastructure;

namespace Nop.Plugin.Api.Services
{
    public class ProductWarehouseInventoriesApiService : IProductWarehouseInventoriesApiService
    {
        private readonly IRepository<ProductWarehouseInventory> _productWarehouseRepository;
        private readonly ProductSyncScope _productScope;

        public ProductWarehouseInventoriesApiService(IRepository<ProductWarehouseInventory> productWarehouseRepository,
            ProductSyncScope productScope)
        {
            _productWarehouseRepository = productWarehouseRepository;
            _productScope = productScope;
        }

        public async Task<IList<ProductWarehouseInventory>> GetMappingsAsync(
            Customer actor, int storeId, int? productId = null,
            int? warehouseId = null, int limit = Constants.Configurations.DefaultLimit,
            int page = Constants.Configurations.DefaultPageValue, int sinceId = Constants.Configurations.DefaultSinceId)
        {
            var products = await _productScope.GetProductsAsync(actor, storeId);
            var query = GetInventoriesQuery(productId, warehouseId, sinceId)
                .Where(inventory => products.Any(product => product.Id == inventory.ProductId))
                .OrderBy(inventory => inventory.Id);

            return await ApiList<ProductWarehouseInventory>.CreateAsync(query, page - 1, limit);
        }

        public async Task<int> GetIvnentoriesCountAsync(int? productId = null, int? warehouseId = null)
        {
            return await GetInventoriesQuery(productId, warehouseId).CountAsync();
        }

        public Task<ProductWarehouseInventory> GetByIdAsync(int id)
        {
            if (id <= 0)
            {
                return null;
            }

            return _productWarehouseRepository.GetByIdAsync(id);
        }

        public async Task<ProductWarehouseInventory> GetScopedByIdAsync(int id, Customer actor, int storeId)
        {
            var products = await _productScope.GetProductsAsync(actor, storeId);
            return await _productWarehouseRepository.Table.FirstOrDefaultAsync(inventory => inventory.Id == id &&
                products.Any(product => product.Id == inventory.ProductId));
        }

        private IQueryable<ProductWarehouseInventory> GetInventoriesQuery(
            int? productId = null,
            int? warehouseId = null, int sinceId = Constants.Configurations.DefaultSinceId)
        {
            var query = _productWarehouseRepository.Table;

            if (productId != null)
            {
                query = query.Where(mapping => mapping.ProductId == productId);
            }

            if (warehouseId != null)
            {
                query = query.Where(mapping => mapping.WarehouseId == warehouseId);
            }

            if (sinceId > 0)
            {
                query = query.Where(mapping => mapping.Id > sinceId);
            }

            return query;
        }
    }
}
