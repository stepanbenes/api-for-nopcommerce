using Nop.Core.Domain.Shipping;

namespace Nop.Plugin.Api.Services;

public interface IWarehouseApiService
{
    Task<IList<Warehouse>> GetWarehousesAsync(IList<int> ids = null, int? productId = null);

    Task<Warehouse> GetWarehouseByIdAsync(int id);
}