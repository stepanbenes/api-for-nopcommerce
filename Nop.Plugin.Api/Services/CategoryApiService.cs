using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Data;
using Nop.Plugin.Api.DataStructures;
using Nop.Plugin.Api.Infrastructure;
using Nop.Services.Catalog;
using Nop.Services.Stores;

namespace Nop.Plugin.Api.Services
{
    public class CategoryApiService : ICategoryApiService
    {
        private readonly IRepository<Category> _categoryRepository;
        private readonly IRepository<ProductCategory> _productCategoryMappingRepository;
        private readonly IStoreContext _storeContext;
        private readonly IStoreMappingService _storeMappingService;
        private readonly ICategoryService _categoryService;

        public CategoryApiService(
                IRepository<Category> categoryRepository,
                IRepository<ProductCategory> productCategoryMappingRepository,
                IStoreMappingService storeMappingService,
            IStoreContext storeContext,
                ICategoryService categoryService)
        {
            _categoryRepository = categoryRepository;
            _productCategoryMappingRepository = productCategoryMappingRepository;
            _storeMappingService = storeMappingService;
            _storeContext = storeContext;
            this._categoryService = categoryService;
        }

        public async Task<IList<Category>> GetCategoriesAsync(
            IList<int> ids = null,
            DateTime? createdAtMin = null, DateTime? createdAtMax = null, DateTime? updatedAtMin = null, DateTime? updatedAtMax = null,
            int limit = Constants.Configurations.DefaultLimit, int page = Constants.Configurations.DefaultPageValue,
            int sinceId = Constants.Configurations.DefaultSinceId,
            int? productId = null,
            bool? publishedStatus = null, int? parentCategoryId = null)
        {
            var query = GetCategoriesQuery(createdAtMin, createdAtMax, updatedAtMin, updatedAtMax, publishedStatus, productId, ids, parentCategoryId);


            if (sinceId > 0)
            {
                query = query.Where(c => c.Id > sinceId);
            }

            query = await _storeMappingService.ApplyStoreMapping(query, (await _storeContext.GetCurrentStoreAsync()).Id);
            return await ApiList<Category>.CreateAsync(query, page - 1, limit);
        }

        public async Task<Category> GetCategoryByIdAsync(int id)
        {
            if (id <= 0)
            {
                return null;
            }

            var category = await _categoryRepository.Table.FirstOrDefaultAsync(cat => cat.Id == id && !cat.Deleted);

            return category;
        }

        public async Task<int> GetCategoriesCountAsync(
            DateTime? createdAtMin = null, DateTime? createdAtMax = null,
            DateTime? updatedAtMin = null, DateTime? updatedAtMax = null,
            bool? publishedStatus = null, int? productId = null, int? parentCategoryId = null)
        {
            var query = GetCategoriesQuery(createdAtMin, createdAtMax, updatedAtMin, updatedAtMax, publishedStatus, productId, ids: null, parentCategoryId);

            query = await _storeMappingService.ApplyStoreMapping(query, (await _storeContext.GetCurrentStoreAsync()).Id);
            return await query.CountAsync();
        }

        public async Task<IDictionary<int, IList<Category>>> GetProductCategories(IList<int> productIds)
        {
            var productCategories = await _categoryService.GetProductCategoryIdsAsync(productIds.ToArray());
            var categoriesByProduct = new Dictionary<int, IList<Category>>();
            foreach (var (productId, categoryIds) in productCategories)
            {
                var categories = new List<Category>();
                foreach (var categoryId in categoryIds)
                    categories.Add(await GetCategoryByIdAsync(categoryId));

                categoriesByProduct.Add(productId, categories);
            }

            return categoriesByProduct;
        }

        private IQueryable<Category> GetCategoriesQuery(
            DateTime? createdAtMin, DateTime? createdAtMax, DateTime? updatedAtMin, DateTime? updatedAtMax,
            bool? publishedStatus, int? productId, IList<int> ids, int? parentCategoryId)
        {
            var query = _categoryRepository.Table;

            if (ids != null && ids.Count > 0)
            {
                query = query.Where(c => ids.Contains(c.Id));
            }

            if (parentCategoryId != null)
            {
                query = query.Where(c => c.ParentCategoryId == parentCategoryId.Value);
            }

            if (publishedStatus != null)
            {
                query = query.Where(c => c.Published == publishedStatus.Value);
            }

            query = query.Where(c => !c.Deleted);

            if (createdAtMin != null)
            {
                query = query.Where(c => c.CreatedOnUtc > createdAtMin.Value);
            }

            if (createdAtMax != null)
            {
                query = query.Where(c => c.CreatedOnUtc < createdAtMax.Value);
            }

            if (updatedAtMin != null)
            {
                query = query.Where(c => c.UpdatedOnUtc > updatedAtMin.Value);
            }

            if (updatedAtMax != null)
            {
                query = query.Where(c => c.UpdatedOnUtc < updatedAtMax.Value);
            }

            if (productId != null)
            {
                var categoryMappingsForProduct = from productCategoryMapping in _productCategoryMappingRepository.Table
                                                 where productCategoryMapping.ProductId == productId
                                                 select productCategoryMapping;

                query = from category in query
                        join productCategoryMapping in categoryMappingsForProduct on category.Id equals productCategoryMapping.CategoryId
                        select category;
            }

            query = query.OrderBy(category => category.Id);

            return query;
        }
    }
}
