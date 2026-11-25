using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Nop.Plugin.Api.Authorization.Requirements;
using Nop.Plugin.Api.Infrastructure;
using Nop.Services.Authentication;
using Nop.Services.Customers;

namespace Nop.Plugin.Api.Authorization.Policies
{
    public class CustomerRoleAuthorizationPolicy(
        IAuthenticationService authenticationService,
        ICustomerService customerService,
        IHttpContextAccessor httpContextAccessor) : AuthorizationHandler<CustomerRoleRequirement>
    {
        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CustomerRoleRequirement requirement)
        {
            var customer = AuthorizationSchemeRequirement.HasBearerCredential(httpContextAccessor.HttpContext?.Request.Headers)
                ? await authenticationService.GetAuthenticatedCustomerAsync()
                : null;
            if (customer is not null &&
                (await customerService.GetCustomerRolesAsync(customer)).Any(role => role.SystemName == Constants.Roles.ApiRoleSystemName))
            {
                context.Succeed(requirement);
            }
            else
            {
                context.Fail();
            }
        }
    }
}
