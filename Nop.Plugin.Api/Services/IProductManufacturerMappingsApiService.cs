using Nop.Core.Domain.Catalog;
using Nop.Plugin.Api.Infrastructure;

namespace Nop.Plugin.Api.Services
{
    public interface IProductManufacturerMappingsApiService
    {
        Task<IList<ProductManufacturer>> GetMappingsAsync(
            int? productId = null, int? manufacturerId = null, int limit = Constants.Configurations.DefaultLimit,
            int page = Constants.Configurations.DefaultPageValue, int sinceId = Constants.Configurations.DefaultSinceId);

        Task<int> GetMappingsCountAsync(int? productId = null, int? manufacturerId = null);

        Task<ProductManufacturer> GetByIdAsync(int id);
    }
}
