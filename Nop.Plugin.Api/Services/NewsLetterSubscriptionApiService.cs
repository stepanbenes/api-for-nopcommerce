using Nop.Core;
using Nop.Core.Domain.Messages;
using Nop.Data;
using Nop.Plugin.Api.DataStructures;
using Nop.Plugin.Api.Infrastructure;

namespace Nop.Plugin.Api.Services
{
    public class NewsLetterSubscriptionApiService : INewsLetterSubscriptionApiService
    {
        private readonly IRepository<NewsLetterSubscription> _newsLetterSubscriptionRepository;
        private readonly IStoreContext _storeContext;

        public NewsLetterSubscriptionApiService(IRepository<NewsLetterSubscription> newsLetterSubscriptionRepository, IStoreContext storeContext)
        {
            _newsLetterSubscriptionRepository = newsLetterSubscriptionRepository;
            _storeContext = storeContext;
        }

        public async Task<List<NewsLetterSubscription>> GetNewsLetterSubscriptionsAsync(
            DateTime? createdAtMin = null, DateTime? createdAtMax = null,
            int limit = Constants.Configurations.DefaultLimit, int page = Constants.Configurations.DefaultPageValue,
            int sinceId = Constants.Configurations.DefaultSinceId,
            bool? onlyActive = true)
        {
            var store = await _storeContext.GetCurrentStoreAsync();
            var query = GetNewsLetterSubscriptionsQuery(store.Id, createdAtMin, createdAtMax, onlyActive);

            if (sinceId > 0)
            {
                query = query.Where(c => c.Id > sinceId);
            }

            return await ApiList<NewsLetterSubscription>.CreateAsync(query, page - 1, limit);
        }

        private IQueryable<NewsLetterSubscription> GetNewsLetterSubscriptionsQuery(
            int currentStoreId, DateTime? createdAtMin = null, DateTime? createdAtMax = null, bool? onlyActive = true)
        {
            var query = _newsLetterSubscriptionRepository.Table.Where(nls => nls.StoreId == currentStoreId);

            if (onlyActive != null && onlyActive == true)
            {
                query = query.Where(nls => nls.Active == onlyActive);
            }

            if (createdAtMin != null)
            {
                query = query.Where(c => c.CreatedOnUtc > createdAtMin.Value);
            }

            if (createdAtMax != null)
            {
                query = query.Where(c => c.CreatedOnUtc < createdAtMax.Value);
            }

            query = query.OrderBy(nls => nls.Id);

            return query;
        }
    }
}
