using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Nop.Core.Domain.Customers;
using Nop.Services.Customers;
using System.Security.Claims;

namespace Nop.Plugin.Api.Services
{
    public class BearerTokenAuthenticationService : Nop.Services.Authentication.IAuthenticationService
    {
        #region Fields

        private readonly ICustomerService customerService;
        private readonly IHttpContextAccessor httpContextAccessor;
        private Task<Customer> authenticatedCustomer;

        #endregion

        #region Ctor

        public BearerTokenAuthenticationService(ICustomerService customerService,
          IHttpContextAccessor httpContextAccessor)
        {
            this.customerService = customerService;
            this.httpContextAccessor = httpContextAccessor;
        }

        #endregion

        #region Methods

        public Task SignInAsync(Customer customer, bool isPersistent)
        {
            return Task.CompletedTask; // bearer token does not support signing in, so it is no-op
        }

        public Task SignOutAsync()
        {
            return Task.CompletedTask; // bearer token does not support signing out, so it is no-op
        }

        public Task<Customer> GetAuthenticatedCustomerAsync() => authenticatedCustomer ??= ResolveCustomerAsync();

        private async Task<Customer> ResolveCustomerAsync()
        {
            //try to get authenticated user identity
            var context = httpContextAccessor.HttpContext;
            if (context is null)
                return null;

            var authenticateResult = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
            if (!authenticateResult.Succeeded)
                return null;

            Customer customer = null;
            var identifierClaim = authenticateResult.Principal.FindFirst(claim => claim.Type == ClaimTypes.NameIdentifier);
            if (identifierClaim != null && Guid.TryParse(identifierClaim.Value, out Guid customerGuid))
            {
                customer = await customerService.GetCustomerByGuidAsync(customerGuid);
            }

            //whether the found customer is available
            if (customer == null || !customer.Active || customer.RequireReLogin || customer.Deleted)
                return null;

            return customer;
        }

        #endregion
    }
}
