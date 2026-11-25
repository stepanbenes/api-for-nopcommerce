using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Nop.Plugin.Api.DTO.Errors;
using Nop.Services.Authentication;
using Nop.Services.Customers;

namespace Nop.Plugin.Api.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class UnpagedCollectionAttribute : ActionFilterAttribute
{
    internal const string ParameterName = "unpaged";

    internal static bool IsEnabled(HttpContext context) =>
        context?.Items.ContainsKey(typeof(UnpagedCollectionAttribute)) == true;

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var query = context.HttpContext.Request.Query;
        if (!query.TryGetValue(ParameterName, out var parameter))
        {
            await next();
            return;
        }

        if (parameter.Count != 1 || !bool.TryParse(parameter, out var unpaged))
        {
            context.Result = Error(StatusCodes.Status400BadRequest, "Supply true or false.");
            return;
        }

        if (unpaged)
        {
            var pagingParameters = query.Keys.Select(key => key[(key.LastIndexOf('.') + 1)..].Replace("_", string.Empty));
            if (pagingParameters.Contains("page", StringComparer.OrdinalIgnoreCase) ||
                pagingParameters.Contains("limit", StringComparer.OrdinalIgnoreCase))
            {
                context.Result = Error(StatusCodes.Status400BadRequest, "Omit both page and limit when requesting an unpaged collection.");
                return;
            }

            var services = context.HttpContext.RequestServices;
            var customer = await services.GetRequiredService<IAuthenticationService>().GetAuthenticatedCustomerAsync();
            if (customer == null || !await services.GetRequiredService<ICustomerService>().IsAdminAsync(customer))
            {
                context.Result = Error(StatusCodes.Status403Forbidden, "Only administrators may request an unpaged collection.");
                return;
            }

            context.HttpContext.Items[typeof(UnpagedCollectionAttribute)] = true;
        }

        await next();
    }

    private static ObjectResult Error(int status, string message) =>
        new(new ErrorsRootObject { Errors = new() { [ParameterName] = [message] } }) { StatusCode = status };
}
