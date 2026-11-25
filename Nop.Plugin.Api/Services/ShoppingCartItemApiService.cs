using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Plugin.Api.DataStructures;
using Nop.Plugin.Api.Infrastructure;

namespace Nop.Plugin.Api.Services
{
    public class ShoppingCartItemApiService : IShoppingCartItemApiService
    {
        private readonly IRepository<ShoppingCartItem> _shoppingCartItemsRepository;
        private readonly IStoreContext _storeContext;
        private readonly ShoppingCartSettings _shoppingCartSettings;

        public ShoppingCartItemApiService(IRepository<ShoppingCartItem> shoppingCartItemsRepository, IStoreContext storeContext,
            ShoppingCartSettings shoppingCartSettings)
        {
            _shoppingCartItemsRepository = shoppingCartItemsRepository;
            _storeContext = storeContext;
            _shoppingCartSettings = shoppingCartSettings;
        }

        public async Task<List<ShoppingCartItem>> GetShoppingCartItemsAsync(
            int? customerId = null, DateTime? createdAtMin = null, DateTime? createdAtMax = null,
            DateTime? updatedAtMin = null, DateTime? updatedAtMax = null, int? limit = null,
            int? page = null, ShoppingCartType? shoppingCartType = null)
        {
            var query = await GetShoppingCartItemsQueryAsync(customerId, createdAtMin, createdAtMax,
                                                  updatedAtMin, updatedAtMax, shoppingCartType);

            return await ApiList<ShoppingCartItem>.CreateAsync(query, (page ?? Constants.Configurations.DefaultPageValue) - 1, limit ?? Constants.Configurations.DefaultLimit);
        }

        public async Task<ShoppingCartItem> GetShoppingCartItemAsync(int id, int? customerId = null)
        {
            var query = await GetShoppingCartItemsQueryAsync(customerId);
            return await query.FirstOrDefaultAsync(item => item.Id == id);
        }

        public async Task<List<ShoppingCartItem>> GetShoppingCartItemsForDeletionAsync(int customerId,
            ShoppingCartType? shoppingCartType, int[] ids)
        {
            var query = await GetShoppingCartItemsQueryAsync(customerId, shoppingCartType: shoppingCartType);
            if (ids.Length > 0)
                query = query.Where(item => ids.Contains(item.Id));

            return await query.ToListAsync();
        }

        private async Task<IQueryable<ShoppingCartItem>> GetShoppingCartItemsQueryAsync(
            int? customerId = null, DateTime? createdAtMin = null, DateTime? createdAtMax = null,
            DateTime? updatedAtMin = null, DateTime? updatedAtMax = null, ShoppingCartType? shoppingCartType = null)
        {
            var query = _shoppingCartItemsRepository.Table;

            if (customerId != null)
            {
                query = query.Where(shoppingCartItem => shoppingCartItem.CustomerId == customerId);
            }

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

            if (shoppingCartType != null)
            {
                query = query.Where(c => c.ShoppingCartTypeId == (int)shoppingCartType.Value);
            }

            if (!_shoppingCartSettings.CartsSharedBetweenStores)
            {
                var store = await _storeContext.GetCurrentStoreAsync();
                query = query.Where(c => c.StoreId == store.Id);
            }

            query = query.OrderBy(shoppingCartItem => shoppingCartItem.Id);

            return query;
        }
    }
}
