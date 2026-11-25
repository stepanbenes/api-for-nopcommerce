using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core.Caching;
using Nop.Plugin.Api.Authorization.Attributes;
using Nop.Plugin.Api.Infrastructure;
using Nop.Services.Authentication;
using Nop.Services.Logging;
using Nop.Services.Security;

namespace Nop.Plugin.Api.Controllers;

[ApiController]
[ApiExplorerSettings(GroupName = "v1")]
[Authorize(Policy = JwtBearerDefaults.AuthenticationScheme, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[AuthorizePermission(StandardPermission.System.MANAGE_MAINTENANCE)]
[Route("api/admin/cache")]
public class CacheMaintenanceController(
    IStaticCacheManager cacheManager,
    ICustomerActivityService customerActivityService,
    IAuthenticationService authenticationService) : ControllerBase
{
    [HttpPost("clear", Name = "ClearCache")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ClearCache()
    {
        var caller = await authenticationService.GetAuthenticatedCustomerAsync();
        var audit = await customerActivityService.InsertActivityAsync(caller, Constants.ClearCacheActivity,
            "Requested a cache clear through the API.");
        if (audit is null)
            return Problem(statusCode: StatusCodes.Status500InternalServerError,
                detail: "The cache clear request could not be recorded in the activity log.");

        await cacheManager.ClearAsync();
        return NoContent();
    }
}
