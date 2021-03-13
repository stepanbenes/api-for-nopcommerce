using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Api.DTO.Customers;
using Nop.Plugin.Api.Infrastructure;

namespace Nop.Plugin.Api.Services
{
    public interface ICustomerApiService
    {
        Task<int> GetCustomersCountAsync();

        Task<CustomerDto> GetCustomerByIdAsync(int id, bool showDeleted = false);

        Task<Customer> GetCustomerEntityByIdAsync(int id);

        Task<IList<CustomerDto>> GetCustomersDtosAsync(
            DateTime? createdAtMin = null, DateTime? createdAtMax = null,
            int limit = Constants.Configurations.DefaultLimit, int page = Constants.Configurations.DefaultPageValue,
            int sinceId = Constants.Configurations.DefaultSinceId);

        Task<IList<CustomerDto>> SearchAsync(
            string query = "", string order = Constants.Configurations.DefaultOrder,
            int page = Constants.Configurations.DefaultPageValue, int limit = Constants.Configurations.DefaultLimit);

        Task<Dictionary<string, string>> GetFirstAndLastNameByCustomerIdAsync(int customerId);
    }
}
