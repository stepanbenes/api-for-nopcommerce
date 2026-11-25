using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Vendors;
using Nop.Data;
using Nop.Plugin.Api.DataStructures;
using Nop.Plugin.Api.Infrastructure;
using Nop.Services.Stores;

namespace Nop.Plugin.Api.Services
{
    public class ProductApiService : IProductApiService
    {
        private readonly IRepository<ProductCategory> _productCategoryMappingRepository;
        private readonly IRepository<Product> _productRepository;
        private readonly IStoreContext _storeContext;
        private readonly IStoreMappingService _storeMappingService;
        private readonly IRepository<Vendor> _vendorRepository;

        public ProductApiService(
            IRepository<Product> productRepository,
            IRepository<ProductCategory> productCategoryMappingRepository,
            IRepository<Vendor> vendorRepository,
            IStoreMappingService storeMappingService,
            IStoreContext storeContext)
        {
            _productRepository = productRepository;
            _productCategoryMappingRepository = productCategoryMappingRepository;
            _vendorRepository = vendorRepository;
            _storeMappingService = storeMappingService;
            _storeContext = storeContext;
        }

        public async Task<IList<Product>> GetProductsAsync(
            IList<int> ids = null,
            DateTime? createdAtMin = null, DateTime? createdAtMax = null, DateTime? updatedAtMin = null, DateTime? updatedAtMax = null,
            int? limit = null, int? page = null,
            int? sinceId = null,
            int? categoryId = null, string vendorName = null, bool? publishedStatus = null, IList<string> manufacturerPartNumbers = null, bool? isDownload = null)
        {
            var query = GetProductsQuery(createdAtMin, createdAtMax, updatedAtMin, updatedAtMax, vendorName, publishedStatus, ids, categoryId, manufacturerPartNumbers, isDownload);

            if (sinceId > 0)
            {
                query = query.Where(c => c.Id > sinceId);
            }

            query = await _storeMappingService.ApplyStoreMapping(query, (await _storeContext.GetCurrentStoreAsync()).Id);
            return await ApiList<Product>.CreateAsync(query, (page ?? Constants.Configurations.DefaultPageValue) - 1, (limit ?? Constants.Configurations.DefaultLimit));
        }

        public async Task<int> GetProductsCountAsync(
            DateTime? createdAtMin = null, DateTime? createdAtMax = null,
            DateTime? updatedAtMin = null, DateTime? updatedAtMax = null, bool? publishedStatus = null, string vendorName = null,
            int? categoryId = null, IList<string> manufacturerPartNumbers = null, bool? isDownload = null)
        {
            var query = GetProductsQuery(createdAtMin, createdAtMax, updatedAtMin, updatedAtMax, vendorName,
                                         publishedStatus, ids: null, categoryId, manufacturerPartNumbers, isDownload);

            query = await _storeMappingService.ApplyStoreMapping(query, (await _storeContext.GetCurrentStoreAsync()).Id);
            return await query.CountAsync();
        }

        public async Task<Product> GetProductByIdAsync(int productId)
        {
            if (productId == 0)
            {
                return null;
            }

            return await _productRepository.Table.FirstOrDefaultAsync(product => product.Id == productId && !product.Deleted);
        }

        public async Task<Product> GetProductByIdNoTrackingAsync(int productId)
        {
            if (productId == 0)
            {
                return null;
            }

            return await _productRepository.Table.FirstOrDefaultAsync(product => product.Id == productId && !product.Deleted);
        }

        private IQueryable<Product> GetProductsQuery(
            DateTime? createdAtMin = null, DateTime? createdAtMax = null,
            DateTime? updatedAtMin = null, DateTime? updatedAtMax = null, string vendorName = null,
            bool? publishedStatus = null, IList<int> ids = null, int? categoryId = null, IList<string> manufacturerPartNumbers = null, bool? isDownload = null)

        {
            var query = _productRepository.Table;

            if (ids != null && ids.Count > 0)
            {
                query = query.Where(p => ids.Contains(p.Id));
            }

            if (manufacturerPartNumbers != null && manufacturerPartNumbers.Count > 0)
            {
                query = query.Where(p => manufacturerPartNumbers.Contains(p.ManufacturerPartNumber));
            }

            if (publishedStatus != null)
            {
                query = query.Where(p => p.Published == publishedStatus.Value);
            }

            if (isDownload != null)
            {
                query = query.Where(p => p.IsDownload == isDownload.Value);
            }

            // always return products that are not deleted!!!
            query = query.Where(p => !p.Deleted);

            if (createdAtMin != null)
            {
                query = query.Where(p => p.CreatedOnUtc > createdAtMin.Value);
            }

            if (createdAtMax != null)
            {
                query = query.Where(p => p.CreatedOnUtc < createdAtMax.Value);
            }

            if (updatedAtMin != null)
            {
                query = query.Where(p => p.UpdatedOnUtc > updatedAtMin.Value);
            }

            if (updatedAtMax != null)
            {
                query = query.Where(p => p.UpdatedOnUtc < updatedAtMax.Value);
            }

            if (!string.IsNullOrEmpty(vendorName))
            {
                query = from vendor in _vendorRepository.Table
                        join product in query on vendor.Id equals product.VendorId
                        where vendor.Name == vendorName && !vendor.Deleted && vendor.Active
                        select product;
            }

            if (categoryId != null)
            {
                var categoryMappingsForProduct = from productCategoryMapping in _productCategoryMappingRepository.Table
                                                 where productCategoryMapping.CategoryId == categoryId
                                                 select productCategoryMapping;

                query = from product in query
                        join productCategoryMapping in categoryMappingsForProduct on product.Id equals productCategoryMapping.ProductId
                        select product;
            }

            query = query.OrderBy(product => product.Id);

            return query;
        }
    }
}
