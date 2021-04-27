using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Nop.Core.Domain.Customers;
using Nop.Services.Customers;

namespace Nop.Plugin.Api.Services
{
	public interface IApiWorkContext
	{
		Task<Customer> GetAuthenticatedCustomerAsync();
	}

	public class ApiWorkContext : IApiWorkContext
	{
		private readonly IHttpContextAccessor httpContextAccessor;
		private readonly ICustomerService customerService;

		public ApiWorkContext(IHttpContextAccessor httpContextAccessor, ICustomerService customerService)
		{
			this.httpContextAccessor = httpContextAccessor;
			this.customerService = customerService;
		}

		public async Task<Customer> GetAuthenticatedCustomerAsync()
		{
			var customerGuidClaim = httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier);
			if (Guid.TryParse(customerGuidClaim?.Value, out Guid customerGuid))
			{
				return await customerService.GetCustomerByGuidAsync(customerGuid);
			}
			return null;
		}
	}
}
