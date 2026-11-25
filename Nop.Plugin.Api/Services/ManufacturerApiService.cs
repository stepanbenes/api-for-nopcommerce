using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Localization;
using Nop.Data;
using Nop.Plugin.Api.DataStructures;
using Nop.Plugin.Api.Infrastructure;

namespace Nop.Plugin.Api.Services
{
    public class ManufacturerApiService : IManufacturerApiService
    {
        private readonly IRepository<LocalizedProperty> _localizedPropertyRepository;
        private readonly IRepository<Manufacturer> _manufacturerRepository;
        private readonly IRepository<ProductManufacturer> _productManufacturerMappingRepository;

        public ManufacturerApiService(
            IRepository<Manufacturer> manufacturerRepository,
            IRepository<ProductManufacturer> productManufacturerMappingRepository,
            IRepository<LocalizedProperty> localizedPropertyRepository)
        {
            _manufacturerRepository = manufacturerRepository;
            _productManufacturerMappingRepository = productManufacturerMappingRepository;
            _localizedPropertyRepository = localizedPropertyRepository;
        }

        public async Task<IList<Manufacturer>> GetManufacturersAsync(
            IList<int> ids = null,
            DateTime? createdAtMin = null, DateTime? createdAtMax = null, DateTime? updatedAtMin = null, DateTime? updatedAtMax = null,
            int limit = Constants.Configurations.DefaultLimit, int page = Constants.Configurations.DefaultPageValue,
            int sinceId = Constants.Configurations.DefaultSinceId,
            int? productId = null, bool? publishedStatus = null, int? languageId = null)
        {
            var query = GetManufacturersQuery(createdAtMin, createdAtMax, updatedAtMin, updatedAtMax, publishedStatus, productId, ids);

            if (sinceId > 0)
            {
                query = query.Where(c => c.Id > sinceId);
            }
            var list = await ApiList<Manufacturer>.CreateAsync(query, page - 1, limit);

            if (languageId is int requestedLanguageId && list.Count > 0)
            {
                var pageIds = list.Select(manufacturer => manufacturer.Id).ToArray();
                var localizedNames = await _localizedPropertyRepository.Table
                    .Where(property => pageIds.Contains(property.EntityId) &&
                        property.LocaleKeyGroup == nameof(Manufacturer) &&
                        property.LocaleKey == nameof(Manufacturer.Name) &&
                        property.LanguageId == requestedLanguageId)
                    .OrderBy(property => property.Id)
                    .ToListAsync();
                var namesByManufacturer = localizedNames.ToLookup(property => property.EntityId);

                foreach (var manufacturer in list)
                {
                    var localizedName = namesByManufacturer[manufacturer.Id].FirstOrDefault();
                    if (localizedName != null)
                        manufacturer.Name = localizedName.LocaleValue;
                }
            }

            return list;
        }

        public async Task<Manufacturer> GetManufacturerByIdAsync(int id)
        {
            if (id <= 0)
            {
                return null;
            }

            var manufacturer = await _manufacturerRepository.Table.FirstOrDefaultAsync(man => man.Id == id && !man.Deleted);

            return manufacturer;
        }

        public async Task<int> GetManufacturersCountAsync(
            DateTime? createdAtMin = null, DateTime? createdAtMax = null,
            DateTime? updatedAtMin = null, DateTime? updatedAtMax = null,
            bool? publishedStatus = null, int? productId = null)
        {
            var query = GetManufacturersQuery(createdAtMin, createdAtMax, updatedAtMin, updatedAtMax,
                                              publishedStatus, productId);

            return await query.CountAsync();
        }

        private IQueryable<Manufacturer> GetManufacturersQuery(
            DateTime? createdAtMin = null, DateTime? createdAtMax = null, DateTime? updatedAtMin = null, DateTime? updatedAtMax = null,
            bool? publishedStatus = null, int? productId = null, IList<int> ids = null)
        {
            var query = _manufacturerRepository.Table;

            if (ids != null && ids.Count > 0)
            {
                query = query.Where(c => ids.Contains(c.Id));
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
                var manufacturerMappingsForProduct = from productManufacturerMapping in _productManufacturerMappingRepository.Table
                                                     where productManufacturerMapping.ProductId == productId
                                                     select productManufacturerMapping;

                query = from manufacturer in query
                        join productManufacturerMapping in manufacturerMappingsForProduct on manufacturer.Id equals productManufacturerMapping.ManufacturerId
                        select manufacturer;
            }

            query = query.OrderBy(manufacturer => manufacturer.Id);

            return query;
        }
    }
}
