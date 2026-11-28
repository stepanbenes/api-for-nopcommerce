using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Nop.Plugin.Api.Attributes;
using Nop.Plugin.Api.Authorization.Attributes;
using Nop.Plugin.Api.Services;
using Nop.Services.Security;

namespace Nop.Plugin.Api.Controllers;

[ApiController]
[Authorize(Policy = JwtBearerDefaults.AuthenticationScheme, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[AuthorizePermission(StandardPermission.Catalog.PRODUCTS_CREATE_EDIT_DELETE)]
public sealed class SyncCapabilitiesController : ControllerBase
{
    [HttpGet("/api/sync/capabilities", Name = "GetSyncCapabilities")]
    [ApiAudience(ApiAudience.BackOffice)]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    [ProducesResponseType(typeof(SyncCapabilities), 200)]
    [ProducesResponseType(typeof(void), 401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(typeof(ProblemDetails), 503)]
    public async Task<IActionResult> GetSyncCapabilities([FromServices] BulkPriceStockAvailability receipts)
    {
        var transactionalWrites = BulkPriceStockService.SupportsTransactionalWrites();
        try
        {
            return Ok(new SyncCapabilities(transactionalWrites && await receipts.IsAvailableAsync(),
                transactionalWrites, transactionalWrites));
        }
        catch (BulkPriceStockBusyException)
        {
            return Problem(statusCode: 503);
        }
    }
}

public sealed record SyncCapabilities(
    [property: JsonProperty("versioned_bulk_price_stock")] bool VersionedBulkPriceStock,
    [property: JsonProperty("desired_price_stock")] bool DesiredPriceStock,
    [property: JsonProperty("warehouse_stock_compare_and_set")] bool WarehouseStockCompareAndSet);
