using Microsoft.AspNetCore.Http;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Api.Authorization.Requirements;
using Nop.Services.Authentication;
using Nop.Services.Customers;

namespace Nop.Plugin.Api.Services
{
    public class BearerTokenOrCookieAuthenticationService : Nop.Services.Authentication.IAuthenticationService
    {
        #region Fields

        private readonly IHttpContextAccessor httpContextAccessor;
        private readonly Nop.Services.Authentication.IAuthenticationService bearerTokenAuthenticationService;
        private readonly Nop.Services.Authentication.IAuthenticationService cookieAuthenticationService;

        #endregion

        #region Ctor

        public BearerTokenOrCookieAuthenticationService(CustomerSettings customerSettings,
          ICustomerService customerService,
          IHttpContextAccessor httpContextAccessor)
        {
            this.httpContextAccessor = httpContextAccessor;
            bearerTokenAuthenticationService = new BearerTokenAuthenticationService(customerService, httpContextAccessor);
            cookieAuthenticationService = new CookieAuthenticationService(customerSettings, customerService, httpContextAccessor);
        }

        #endregion

        #region Methods

        /// <summary>
        /// Sign in
        /// </summary>
        /// <param name="customer">Customer</param>
        /// <param name="isPersistent">Whether the authentication session is persisted across multiple requests</param>
        public async Task SignInAsync(Customer customer, bool isPersistent)
        {
            await bearerTokenAuthenticationService.SignInAsync(customer, isPersistent);
            await cookieAuthenticationService.SignInAsync(customer, isPersistent);
        }

        /// <summary>
        /// Sign out
        /// </summary>
        public async Task SignOutAsync()
        {
            await bearerTokenAuthenticationService.SignOutAsync();
            await cookieAuthenticationService.SignOutAsync();
        }

        public Task<Customer> GetAuthenticatedCustomerAsync()
        {
            return AuthorizationSchemeRequirement.HasBearerCredential(httpContextAccessor.HttpContext?.Request.Headers)
                ? bearerTokenAuthenticationService.GetAuthenticatedCustomerAsync()
                : cookieAuthenticationService.GetAuthenticatedCustomerAsync();
        }

        #endregion
    }
}
