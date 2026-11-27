using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Api.Authorization.Attributes;
using Nop.Plugin.Api.DTO.Products;
using Nop.Plugin.Api.Services;
using Nop.Services.Authentication;
using Nop.Services.Security;

namespace Nop.Plugin.Api.Controllers;

[ApiController]
[Authorize(Policy = JwtBearerDefaults.AuthenticationScheme, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[AuthorizePermission(StandardPermission.Catalog.PRODUCTS_CREATE_EDIT_DELETE)]
[Route("/api/products/{productId:int}/pictures")]
public sealed class ProductPictureSyncController(
    IAuthenticationService authenticationService,
    IStoreContext storeContext,
    ProductPictureSyncService pictureSyncService) : ControllerBase
{
    [HttpGet("sync/{key}", Name = "GetProductPictureDigest")]
    [ProducesResponseType(typeof(ProductPictureSyncResult), 200)]
    [ProducesResponseType(typeof(ProductPictureSyncResult), 409)]
    [ProducesResponseType(404)]
    public Task<IActionResult> Get(
        [Range(1, int.MaxValue)] int productId,
        [RegularExpression("^[a-z0-9_-]{1,64}$")] string key,
        CancellationToken cancellationToken) =>
        ExecuteAsync(productId, key, null, cancellationToken);

    [HttpPut("sync/{key}", Name = "SyncProductPicture")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [ProducesResponseType(typeof(ProductPictureSyncResult), 200)]
    [ProducesResponseType(typeof(ProductPictureSyncResult), 400)]
    [ProducesResponseType(typeof(ProductPictureSyncResult), 409)]
    [ProducesResponseType(typeof(ProductPictureSyncResult), 503)]
    [ProducesResponseType(404)]
    [ProducesResponseType(413)]
    public Task<IActionResult> Put(
        [Range(1, int.MaxValue)] int productId,
        [RegularExpression("^[a-z0-9_-]{1,64}$")] string key,
        [FromBody, Required] ProductPictureSyncRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(productId, key, request, cancellationToken);

    [HttpGet("{mappingId:int}/{pictureId:int}/sync", Name = "GetLinkedProductPictureDigest")]
    [ProducesResponseType(typeof(ProductPictureSyncResult), 200)]
    [ProducesResponseType(typeof(ProductPictureSyncResult), 409)]
    [ProducesResponseType(404)]
    public Task<IActionResult> GetLinked(
        [Range(1, int.MaxValue)] int productId,
        [Range(1, int.MaxValue)] int mappingId,
        [Range(1, int.MaxValue)] int pictureId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(productId, null, null, cancellationToken, mappingId, pictureId);

    [HttpPut("{mappingId:int}/{pictureId:int}/sync", Name = "SyncLinkedProductPicture")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [ProducesResponseType(typeof(ProductPictureSyncResult), 200)]
    [ProducesResponseType(typeof(ProductPictureSyncResult), 400)]
    [ProducesResponseType(typeof(ProductPictureSyncResult), 409)]
    [ProducesResponseType(typeof(ProductPictureSyncResult), 503)]
    [ProducesResponseType(404)]
    [ProducesResponseType(413)]
    public Task<IActionResult> PutLinked(
        [Range(1, int.MaxValue)] int productId,
        [Range(1, int.MaxValue)] int mappingId,
        [Range(1, int.MaxValue)] int pictureId,
        [FromBody, Required] ProductPictureSyncRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(productId, null, request, cancellationToken, mappingId, pictureId);

    private async Task<IActionResult> ExecuteAsync(int productId, string key, ProductPictureSyncRequest request,
        CancellationToken cancellationToken, int? mappingId = null, int? pictureId = null)
    {
        var actor = await authenticationService.GetAuthenticatedCustomerAsync();
        if (actor == null)
            return Unauthorized();

        var store = await storeContext.GetCurrentStoreAsync();
        var result = await pictureSyncService.ExecuteAsync(actor, store.Id, productId, key, request, cancellationToken, mappingId, pictureId);
        return result.Status switch
        {
            "not_found" => NotFound(),
            "invalid_image" => BadRequest(result),
            "conflict" or "ambiguous" => Conflict(result),
            "unsupported_storage" or "busy" or "thumbnail_pending" => StatusCode(503, result),
            _ => Ok(result)
        };
    }
}
