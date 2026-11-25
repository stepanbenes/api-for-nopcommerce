using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Messages;
using Nop.Data;
using Nop.Plugin.Api.DataStructures;
using Nop.Plugin.Api.DTO.Customers;
using Nop.Plugin.Api.Helpers;
using Nop.Plugin.Api.Infrastructure;
using Nop.Plugin.Api.MappingExtensions;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;
using System.Linq.Dynamic.Core;
using System.Text.RegularExpressions;

namespace Nop.Plugin.Api.Services
{
    public class CustomerApiService : ICustomerApiService
    {
        private readonly IStaticCacheManager _cacheManager;
        private readonly IAddressApiService _addressApiService;
        private readonly ICurrencyService _currencyService;
        private readonly IRepository<Customer> _customerRepository;
        private readonly IRepository<GenericAttribute> _genericAttributeRepository;
        private readonly ILanguageService _languageService;
        private readonly ICustomerService _customerService;

        private readonly IStoreContext _storeContext;
        private readonly IRepository<NewsLetterSubscription> _subscriptionRepository;

        private static readonly string EU_COOKIE_LAW_ACCEPTED =
          NopCustomerDefaults.EuCookieLawAcceptedAttribute.ToLowerInvariant();

        private static readonly Dictionary<string, string> CustomerSortFields = typeof(CustomerDto).GetProperties()
            .Where(property => (property.PropertyType == typeof(string) || property.PropertyType.IsValueType) &&
                typeof(Customer).GetProperty(property.Name) != null)
            .ToDictionary(property => property.Name, property => $"Customer.{property.Name}", StringComparer.OrdinalIgnoreCase);

        public CustomerApiService(
          IRepository<Customer> customerRepository,
          IRepository<GenericAttribute> genericAttributeRepository,
          IRepository<NewsLetterSubscription> subscriptionRepository,
          IStoreContext storeContext,
          ILanguageService languageService,
          IStaticCacheManager staticCacheManager,
          IAddressApiService addressApiService,
          ICurrencyService currencyService, ICustomerService customerService)
        {
            _customerRepository = customerRepository;
            _genericAttributeRepository = genericAttributeRepository;
            _subscriptionRepository = subscriptionRepository;
            _storeContext = storeContext;
            _languageService = languageService;
            _cacheManager = staticCacheManager;
            _addressApiService = addressApiService;
            _currencyService = currencyService;
            _customerService = customerService;
        }

        public async Task<IList<CustomerDto>> GetCustomersDtosAsync(
          DateTime? createdAtMin = null, DateTime? createdAtMax = null, int limit = Constants.Configurations.DefaultLimit,
          int page = Constants.Configurations.DefaultPageValue, int sinceId = Constants.Configurations.DefaultSinceId)
        {
            var currentStore = await _storeContext.GetCurrentStoreAsync();
            var query = GetCustomersQuery(currentStore.Id, createdAtMin, createdAtMax, sinceId);

            var result = await GetCustomerPageAsync(query, limit, page, Constants.Configurations.DefaultOrder);
            await SetNewsletterSubscriptionStatusAsync(result);

            return result;
        }

        public async Task<int> GetCustomersCountAsync()
        {
            var storeId = (await _storeContext.GetCurrentStoreAsync()).Id;
            return await GetStoreCustomersQuery(storeId).CountAsync();
        }

        // Need to work with dto object so we can map the first and last name from generic attributes table.
        public async Task<IList<CustomerDto>> SearchAsync(
          string queryParams = "", string order = Constants.Configurations.DefaultOrder,
          int page = Constants.Configurations.DefaultPageValue, int limit = Constants.Configurations.DefaultLimit)
        {
            IList<CustomerDto> result = new List<CustomerDto>();

            var searchParams = EnsureSearchQueryIsValid(queryParams, ParseSearchQuery);

            if (searchParams != null)
            {
                var storeId = (await _storeContext.GetCurrentStoreAsync()).Id;
                var query = GetStoreCustomersQuery(storeId);

                foreach (var searchParam in searchParams)
                    // Skip non existing properties.
                    if (ReflectionHelper.HasProperty(searchParam.Key, typeof(Customer)))
                    {
                        // @0 is a placeholder used by dynamic linq and it is used to prevent possible sql injections.
                        query = query.Where(string.Format("{0} = @0 || {0}.Contains(@0)", searchParam.Key), searchParam.Value);
                    }

                if (searchParams.TryGetValue(EU_COOKIE_LAW_ACCEPTED, out var cookieConsent))
                {
                    query = query.Where(customer => _genericAttributeRepository.Table.Any(attribute =>
                        attribute.EntityId == customer.Id && attribute.KeyGroup == nameof(Customer) &&
                        attribute.Key.ToLower() == EU_COOKIE_LAW_ACCEPTED && attribute.Value == cookieConsent));
                }

                result = await GetCustomerPageAsync(query, limit, page, order);
            }

            return result;
        }


        public async Task<Customer> GetCustomerEntityByIdAsync(int id)
        {
            var customer = await _customerRepository.Table.FirstOrDefaultAsync(c => c.Id == id && !c.Deleted);

            return customer;
        }

        public async Task<CustomerDto> GetCustomerByIdAsync(int id, bool showDeleted = false)
        {
            if (id == 0)
            {
                return null;
            }

            // Here we expect to get two records, one for the first name and one for the last name.
            var customerAttributeMappings = await (from c in _customerRepository.Table //NoTracking
                                                   join a in _genericAttributeRepository.Table //NoTracking
                                                     on c.Id equals a.EntityId
                                                   where c.Id == id && (showDeleted || !c.Deleted) &&
                                                         a.KeyGroup == nameof(Customer)
                                                   select new CustomerAttributeMappingDto
                                                   {
                                                       Attribute = a,
                                                       Customer = c
                                                   }).ToListAsync();

            Customer customer = null;
            CustomerDto customerDto = null;

            // This is in case we have first and last names set for the customer.
            if (customerAttributeMappings.Count > 0)
            {
                customer = customerAttributeMappings.First().Customer;
                // The customer object is the same in all mappings.
                customerDto = customer.ToDto();


                foreach (var mapping in customerAttributeMappings)
                {
                    if (mapping.Attribute == null)
                    {
                        continue;
                    }

                    if (mapping.Attribute.Key.Equals(EU_COOKIE_LAW_ACCEPTED, StringComparison.InvariantCultureIgnoreCase))
                    {
                        customerDto.EuCookieLawAccepted = string.IsNullOrWhiteSpace(mapping.Attribute.Value)
                          ? (bool?)null
                          : bool.Parse(mapping.Attribute.Value);
                    }
                }
            }
            else
            {
                // This is when we do not have first and last name set.
                customer = await _customerRepository.Table.FirstOrDefaultAsync(c => c.Id == id && (showDeleted || !c.Deleted));
                if (customer != null)
                    customerDto = customer.ToDto();
            }

            await SetNewsletterSubscriptionStatusAsync(customerDto);

            await SetCustomerDetailsAsync(customer, customerDto);

            return customerDto;
        }

        private Dictionary<string, string> EnsureSearchQueryIsValid(string query,
          Func<string, Dictionary<string, string>> parseSearchQuery)
        {
            if (!string.IsNullOrEmpty(query))
            {
                return parseSearchQuery(query);
            }

            return null;
        }

        private Dictionary<string, string> ParseSearchQuery(string query)
        {
            var parsedQuery = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var splitPattern = @"([\w.]+):";

            var fieldValueList = Regex.Split(query, splitPattern).Where(s => s != string.Empty).ToList();

            if (fieldValueList.Count < 2)
            {
                return parsedQuery;
            }

            for (var i = 0;
                 i < fieldValueList.Count;
                 i += 2)
            {
                var field = fieldValueList[i];
                var value = fieldValueList[i + 1];

                if (!string.IsNullOrEmpty(field) && !string.IsNullOrEmpty(value))
                {
                    field = field.Replace("_", string.Empty);
                    parsedQuery.Add(field.Trim(), value.Trim());
                }
            }

            return parsedQuery;
        }

        private async Task<IList<CustomerDto>> GetCustomerPageAsync(
            IQueryable<Customer> query, int limit, int page, string order)
        {
            var orderedFields = GetCustomerOrder(order);
            var sortedCustomers = query.Select(customer => new
            {
                Customer = customer,
                CookieConsent = _genericAttributeRepository.Table
                    .Where(attribute => attribute.EntityId == customer.Id && attribute.KeyGroup == nameof(Customer) &&
                        attribute.Key.ToLower() == EU_COOKIE_LAW_ACCEPTED)
                    .OrderByDescending(attribute => attribute.Id)
                    .Select(attribute => attribute.Value == null ? null : attribute.Value.Trim().ToLower())
                    .FirstOrDefault()
            }).Select(row => new { row.Customer, row.CookieConsent, HasCookieConsent = row.CookieConsent != null })
                .OrderBy(orderedFields).Select(row => row.Customer.Id);
            var ids = await ApiList<int>.CreateAsync(sortedCustomers, page - 1, limit);
            if (ids.Count == 0)
                return new List<CustomerDto>();

            var customers = (await query.Where(customer => ids.Contains(customer.Id)).ToListAsync())
                .ToDictionary(customer => customer.Id);
            var attributes = await _genericAttributeRepository.Table
                .Where(attribute => ids.Contains(attribute.EntityId) && attribute.KeyGroup == nameof(Customer) &&
                    attribute.Key.ToLower() == EU_COOKIE_LAW_ACCEPTED)
                .OrderBy(attribute => attribute.Id)
                .ToListAsync();
            var attributesByCustomer = attributes.ToLookup(attribute => attribute.EntityId);

            var result = new List<CustomerDto>();
            foreach (var id in ids)
            {
                if (!customers.TryGetValue(id, out var customer))
                    continue;

                var dto = customer.ToDto();
                foreach (var attribute in attributesByCustomer[customer.Id])
                    dto.EuCookieLawAccepted = string.IsNullOrWhiteSpace(attribute.Value)
                        ? (bool?)null
                        : bool.Parse(attribute.Value);

                await SetCustomerDetailsAsync(customer, dto);
                result.Add(dto);
            }

            return result;
        }

        private static string GetCustomerOrder(string order)
        {
            var fields = new List<string>();
            foreach (var term in (string.IsNullOrWhiteSpace(order) ? Constants.Configurations.DefaultOrder : order).Split(','))
            {
                var parts = term.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                var direction = parts.Length == 2 ? parts[1].ToLowerInvariant() : "asc";
                if (parts.Length is 0 or > 2 || direction is not ("asc" or "ascending" or "desc" or "descending"))
                    throw new ArgumentException("Use a customer field followed by an optional asc or desc.", nameof(order));

                var name = parts[0].Replace("_", string.Empty);
                var descending = direction is "desc" or "descending";
                if (name.Equals(nameof(CustomerDto.SubscribedToNewsletter), StringComparison.OrdinalIgnoreCase))
                    continue; // Search has historically returned the default value for this field.

                if (name.Equals(nameof(CustomerDto.EuCookieLawAccepted), StringComparison.OrdinalIgnoreCase))
                {
                    fields.Add(descending ? "HasCookieConsent desc" : "HasCookieConsent asc");
                    fields.Add(descending ? "CookieConsent desc" : "CookieConsent asc");
                }
                else if (CustomerSortFields.TryGetValue(name, out var field))
                {
                    fields.Add(descending ? $"{field} desc" : $"{field} asc");
                }
                else
                {
                    throw new ArgumentException("Use a customer field followed by an optional asc or desc.", nameof(order));
                }
            }

            fields.Add("Customer.Id asc");
            return string.Join(", ", fields);
        }

        private IQueryable<Customer> GetStoreCustomersQuery(int storeId)
        {
            return _customerRepository.Table.Where(customer => !customer.Deleted &&
                (customer.RegisteredInStoreId == 0 || customer.RegisteredInStoreId == storeId));
        }

        private IQueryable<Customer> GetCustomersQuery(int currentStoreId, DateTime? createdAtMin = null, DateTime? createdAtMax = null,
          int sinceId = 0)
        {
            var query = GetStoreCustomersQuery(currentStoreId)
                .Where(customer => !customer.IsSystemAccount && customer.Active);

            if (createdAtMin != null)
            {
                query = query.Where(c => c.CreatedOnUtc > createdAtMin.Value);
            }

            if (createdAtMax != null)
            {
                query = query.Where(c => c.CreatedOnUtc < createdAtMax.Value);
            }

            if (sinceId > 0)
            {
                query = query.Where(customer => customer.Id > sinceId);
            }

            return query;
        }

        private async Task SetNewsletterSubscriptionStatusAsync(IList<CustomerDto> customerDtos)
        {
            if (customerDtos == null)
            {
                return;
            }

            var allNewsletterCustomerEmail = await GetAllNewsletterCustomersEmailsAsync();

            foreach (var customerDto in customerDtos)
                await SetNewsletterSubscriptionStatusAsync(customerDto, allNewsletterCustomerEmail);
        }

        private async Task SetNewsletterSubscriptionStatusAsync(BaseCustomerDto customerDto,
          IEnumerable<string> allNewsletterCustomerEmail = null)
        {
            if (customerDto == null || string.IsNullOrEmpty(customerDto.Email))
            {
                return;
            }

            if (allNewsletterCustomerEmail == null)
            {
                allNewsletterCustomerEmail = await GetAllNewsletterCustomersEmailsAsync();
            }

            if (allNewsletterCustomerEmail != null &&
                allNewsletterCustomerEmail.Contains(customerDto.Email.ToLowerInvariant()))
            {
                customerDto.SubscribedToNewsletter = true;
            }
        }

        private async Task<List<string>> GetAllNewsletterCustomersEmailsAsync()
        {
            var currentStoreId = (await _storeContext.GetCurrentStoreAsync()).Id;
            return await _cacheManager.GetAsync(Constants.Configurations.NEWSLETTER_SUBSCRIBERS_KEY, async () =>
            {
                var subscriberEmails = await (from nls in _subscriptionRepository.Table
                                              where nls.StoreId == currentStoreId && nls.Active
                                              select nls.Email).ToListAsync();
                return subscriberEmails.Where(e => !string.IsNullOrEmpty(e)).Select(e => e.ToLowerInvariant()).ToList();
            });
        }

        private async Task SetCustomerDetailsAsync(Customer customer, CustomerDto customerDto)
        {
            if (customer == null || customerDto == null)
                return;

            customerDto.RoleIds = (await _customerService.GetCustomerRolesAsync(customer, showHidden: true))
                .Select(role => role.Id).ToList();
            customerDto.Addresses = await _addressApiService.GetAddressesByCustomerIdAsync(customer.Id);
            if (customer.BillingAddressId != null)
            {
                customerDto.BillingAddress =
                  await _addressApiService.GetCustomerAddressAsync(customer.Id, customer.BillingAddressId.Value);
            }
            else
            {
                customerDto.BillingAddress = null;
            }

            if (customer.ShippingAddressId != null)
            {
                customerDto.ShippingAddress =
                  await _addressApiService.GetCustomerAddressAsync(customer.Id, customer.ShippingAddressId.Value);
            }
            else
            {
                customerDto.ShippingAddress = null;
            }
        }

        public async Task<Language> GetCustomerLanguageAsync(Customer customer)
        {
            var customerLanguage = await _languageService.GetLanguageByIdAsync(customer?.LanguageId ?? 0);
            return customerLanguage;
        }

        public async Task SetCustomerLanguageAsync(Customer customer, Language language)
        {
            customer.LanguageId = language?.Id ?? 0;
            await _customerService.UpdateCustomerAsync(customer);
        }

        public async Task<Currency> GetCustomerCurrencyAsync(Customer customer)
        {
            var customerCurrency = await _currencyService.GetCurrencyByIdAsync(customer?.CurrencyId ?? 0);
            return customerCurrency;
        }

        public async Task SetCustomerCurrencyAsync(Customer customer, Currency currency)
        {
            customer.CurrencyId = currency.Id;
            await _customerService.UpdateCustomerAsync(customer);
        }
    }
}