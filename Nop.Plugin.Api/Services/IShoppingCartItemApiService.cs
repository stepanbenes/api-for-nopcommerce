using Nop.Core.Domain.Orders;

namespace Nop.Plugin.Api.Services
{
    public interface IShoppingCartItemApiService
    {
        Task<List<ShoppingCartItem>> GetShoppingCartItemsAsync(
            int? customerId = null, DateTime? createdAtMin = null, DateTime? createdAtMax = null,
            DateTime? updatedAtMin = null, DateTime? updatedAtMax = null, int? limit = null,
            int? page = null, ShoppingCartType? shoppingCartType = null);

        Task<ShoppingCartItem> GetShoppingCartItemAsync(int id, int? customerId = null);

        Task<List<ShoppingCartItem>> GetShoppingCartItemsForDeletionAsync(int customerId,
            ShoppingCartType? shoppingCartType, int[] ids);
    }
}
