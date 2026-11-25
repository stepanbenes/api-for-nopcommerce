using Nop.Core.Domain.Catalog;
using Nop.Plugin.Api.Infrastructure;

namespace Nop.Plugin.Api.Services
{
    public interface IProductAttributesApiService
    {
        Task<IList<ProductAttribute>> GetProductAttributesAsync(
            int limit = Constants.Configurations.DefaultLimit,
            int page = Constants.Configurations.DefaultPageValue, int sinceId = Constants.Configurations.DefaultSinceId);

        Task<int> GetProductAttributesCountAsync();

        Task<ProductAttribute> GetByIdAsync(int id);
    }
}
