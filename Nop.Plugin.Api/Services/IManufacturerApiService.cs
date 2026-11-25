using Nop.Core.Domain.Catalog;
using Nop.Plugin.Api.Infrastructure;

namespace Nop.Plugin.Api.Services
{
    public interface IManufacturerApiService
    {
        Task<Manufacturer> GetManufacturerByIdAsync(int manufacturerId);

        Task<IList<Manufacturer>> GetManufacturersAsync(
            IList<int> ids = null,
            DateTime? createdAtMin = null, DateTime? createdAtMax = null, DateTime? updatedAtMin = null, DateTime? updatedAtMax = null,
            int limit = Constants.Configurations.DefaultLimit, int page = Constants.Configurations.DefaultPageValue,
            int sinceId = Constants.Configurations.DefaultSinceId,
            int? productId = null, bool? publishedStatus = null, int? languageId = null);

        Task<int> GetManufacturersCountAsync(
            DateTime? createdAtMin = null, DateTime? createdAtMax = null, DateTime? updatedAtMin = null, DateTime? updatedAtMax = null,
            bool? publishedStatus = null, int? productId = null);
    }
}
