using Nop.Core.Domain.Catalog;
using Nop.Data;
using Nop.Plugin.Api.DataStructures;
using Nop.Plugin.Api.Infrastructure;

namespace Nop.Plugin.Api.Services
{
    public class ProductManufacturerMappingsApiService : IProductManufacturerMappingsApiService
    {
        private readonly IRepository<ProductManufacturer> _productManufacturerMappingsRepository;

        public ProductManufacturerMappingsApiService(IRepository<ProductManufacturer> productManufacturerMappingsRepository)
        {
            _productManufacturerMappingsRepository = productManufacturerMappingsRepository;
        }

        public async Task<IList<ProductManufacturer>> GetMappingsAsync(
            int? productId = null,
            int? manufacturerId = null, int limit = Constants.Configurations.DefaultLimit,
            int page = Constants.Configurations.DefaultPageValue, int sinceId = Constants.Configurations.DefaultSinceId)
        {
            var query = GetMappingsQuery(productId, manufacturerId, sinceId);

            return await ApiList<ProductManufacturer>.CreateAsync(query, page - 1, limit);
        }

        public async Task<int> GetMappingsCountAsync(int? productId = null, int? manufacturerId = null)
        {
            return await GetMappingsQuery(productId, manufacturerId).CountAsync();
        }

        public Task<ProductManufacturer> GetByIdAsync(int id)
        {
            if (id <= 0)
            {
                return null;
            }

            return _productManufacturerMappingsRepository.GetByIdAsync(id);
        }

        private IQueryable<ProductManufacturer> GetMappingsQuery(
            int? productId = null,
            int? manufacturerId = null, int sinceId = Constants.Configurations.DefaultSinceId)
        {
            var query = _productManufacturerMappingsRepository.Table;

            if (productId != null)
            {
                query = query.Where(mapping => mapping.ProductId == productId);
            }

            if (manufacturerId != null)
            {
                query = query.Where(mapping => mapping.ManufacturerId == manufacturerId);
            }

            if (sinceId > 0)
            {
                query = query.Where(mapping => mapping.Id > sinceId);
            }

            query = query.OrderBy(mapping => mapping.Id);

            return query;
        }
    }
}
