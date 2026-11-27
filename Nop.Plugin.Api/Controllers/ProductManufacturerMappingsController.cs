using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Api.Attributes;
using Nop.Plugin.Api.Authorization.Attributes;
using Nop.Plugin.Api.Delta;
using Nop.Plugin.Api.DTO.Errors;
using Nop.Plugin.Api.DTO.ProductManufacturerMappings;
using Nop.Plugin.Api.Infrastructure;
using Nop.Plugin.Api.JSON.ActionResults;
using Nop.Plugin.Api.JSON.Serializers;
using Nop.Plugin.Api.MappingExtensions;
using Nop.Plugin.Api.ModelBinders;
using Nop.Plugin.Api.Models.ProductManufacturerMappingsParameters;
using Nop.Plugin.Api.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Discounts;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Media;
using Nop.Services.Security;
using Nop.Services.Stores;
using System.Net;

namespace Nop.Plugin.Api.Controllers
{
    [AuthorizePermission(StandardPermission.Catalog.PRODUCTS_CREATE_EDIT_DELETE)]
    [AuthorizePermission(StandardPermission.Catalog.MANUFACTURER_CREATE_EDIT_DELETE)]
    public class ProductManufacturerMappingsController : BaseApiController
    {
        private readonly RelationshipWrites<ProductManufacturer> _writes;
        private readonly RelationshipWriteScope _scope;
        private readonly IManufacturerService _manufacturerService;
        private readonly IManufacturerApiService _manufacturerApiService;
        private readonly IProductManufacturerMappingsApiService _productManufacturerMappingsService;

        public ProductManufacturerMappingsController(
            IProductManufacturerMappingsApiService productManufacturerMappingsService,
            IManufacturerService manufacturerService,
            IJsonFieldsSerializer jsonFieldsSerializer,
            IAclService aclService,
            ICustomerService customerService,
            IStoreMappingService storeMappingService,
            IStoreService storeService,
            IDiscountService discountService,
            ICustomerActivityService customerActivityService,
            ILocalizationService localizationService,
            IManufacturerApiService manufacturerApiService,
            IPictureService pictureService,
            RelationshipWrites<ProductManufacturer> writes,
            RelationshipWriteScope scope)
            : base(jsonFieldsSerializer, aclService, customerService, storeMappingService, storeService, discountService, customerActivityService,
                   localizationService, pictureService)
        {
            _productManufacturerMappingsService = productManufacturerMappingsService;
            _manufacturerApiService = manufacturerApiService;
            _manufacturerService = manufacturerService;
            _writes = writes;
            _scope = scope;
        }

        /// <summary>
        ///     Receive a list of all Product-Manufacturer mappings
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="400">Bad Request</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/product_manufacturer_mappings", Name = "GetProductManufacturerMappings")]
        [ProducesResponseType(typeof(ProductManufacturerMappingsRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [GetRequestsErrorInterceptorActionFilter]
        [UnpagedCollection]
        public async Task<IActionResult> GetMappings([FromQuery] ProductManufacturerMappingsParametersModel parameters)
        {
            if (parameters.Limit < Constants.Configurations.MinLimit || parameters.Limit > Constants.Configurations.MaxLimit)
            {
                return Error(HttpStatusCode.BadRequest, "limit", "invalid limit parameter");
            }

            if (parameters.Page < Constants.Configurations.DefaultPageValue)
            {
                return Error(HttpStatusCode.BadRequest, "page", "invalid page parameter");
            }

            IList<ProductManufacturerMappingsDto> mappingsAsDtos =
                (await _productManufacturerMappingsService.GetMappingsAsync(parameters.ProductId,
                                                                parameters.ManufacturerId,
                                                                GetCollectionLimit(parameters.Limit),
                                                                parameters.Page,
                                                                parameters.SinceId)).Select(x => x.ToDto()).ToList();

            var productManufacturerMappingRootObject = new ProductManufacturerMappingsRootObject
            {
                ProductManufacturerMappingsDtos = mappingsAsDtos
            };

            var json = JsonFieldsSerializer.Serialize(productManufacturerMappingRootObject, parameters.Fields);

            return new RawJsonActionResult(json);
        }

        /// <summary>
        ///     Receive a count of all Product-Manufacturer mappings
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/product_manufacturer_mappings/count", Name = "GetProductManufacturerMappingsCount")]
        [ProducesResponseType(typeof(ProductManufacturerMappingsCountRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [GetRequestsErrorInterceptorActionFilter]
        public async Task<IActionResult> GetMappingsCount([FromQuery] ProductManufacturerMappingsCountParametersModel parameters)
        {
            if (parameters.ProductId < 0)
            {
                return Error(HttpStatusCode.BadRequest, "product_id", "invalid product_id");
            }

            if (parameters.ManufacturerId < 0)
            {
                return Error(HttpStatusCode.BadRequest, "manufacturer_id", "invalid manufacturer_id");
            }

            var mappingsCount = await _productManufacturerMappingsService.GetMappingsCountAsync(parameters.ProductId,
                                                                                     parameters.ManufacturerId);

            var productManufacturerMappingsCountRootObject = new ProductManufacturerMappingsCountRootObject
            {
                Count = mappingsCount
            };

            return Ok(productManufacturerMappingsCountRootObject);
        }

        /// <summary>
        ///     Retrieve Product-Manufacturer mappings by spcified id
        /// </summary>
        /// ///
        /// <param name="id">Id of the Product-Manufacturer mapping</param>
        /// <param name="fields">Fields from the Product-Manufacturer mapping you want your json to contain</param>
        /// <response code="200">OK</response>
        /// <response code="404">Not Found</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/product_manufacturer_mappings/{id}", Name = "GetProductManufacturerMappingById")]
        [ProducesResponseType(typeof(ProductManufacturerMappingsRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [GetRequestsErrorInterceptorActionFilter]
        public async Task<IActionResult> GetMappingById([FromRoute] int id, [FromQuery] string fields = "")
        {
            if (id <= 0)
            {
                return Error(HttpStatusCode.BadRequest, "id", "invalid id");
            }

            var mapping = await _productManufacturerMappingsService.GetByIdAsync(id);

            if (mapping == null)
            {
                return Error(HttpStatusCode.NotFound, "product_manufacturer_mapping", "not found");
            }

            var productManufacturerMappingsRootObject = new ProductManufacturerMappingsRootObject();
            productManufacturerMappingsRootObject.ProductManufacturerMappingsDtos.Add(mapping.ToDto());

            var json = JsonFieldsSerializer.Serialize(productManufacturerMappingsRootObject, fields);

            return new RawJsonActionResult(json);
        }

        [HttpPost]
        [Route("/api/product_manufacturer_mappings", Name = "CreateProductManufacturerMapping")]
        [ProducesResponseType(typeof(ProductManufacturerMappingsRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.Conflict)]
        [ProducesResponseType(typeof(ErrorsRootObject), 422)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.ServiceUnavailable)]
        public async Task<IActionResult> CreateProductManufacturerMapping(
            [FromBody]
            [ModelBinder(typeof(JsonModelBinder<ProductManufacturerMappingsDto>))]
            Delta<ProductManufacturerMappingsDto> delta)
        {
            if (!ModelState.IsValid)
                return Error();

            var dto = delta.Dto;
            if (dto.ProductId is not > 0 || dto.ManufacturerId is not > 0)
                return Error(HttpStatusCode.BadRequest, "product_manufacturer_mapping", "positive product_id and manufacturer_id are required");

            return await _writes.ExecuteAsync(async onCommit =>
            {
                var scopeError = await CheckMappingScopeAsync(dto.ProductId.Value, dto.ManufacturerId.Value);
                if (scopeError != null)
                    return scopeError;

                var existing = await FindMappingsAsync(dto.ProductId.Value, dto.ManufacturerId.Value);
                if (existing.Count != 0)
                {
                    var mapping = existing[0];
                    if (existing.Count > 1 ||
                        dto.IsFeaturedProduct.HasValue && dto.IsFeaturedProduct.Value != mapping.IsFeaturedProduct ||
                        dto.DisplayOrder.HasValue && dto.DisplayOrder.Value != mapping.DisplayOrder)
                        return Error(HttpStatusCode.Conflict, "product_manufacturer_mapping", "existing mapping conflicts with the request");

                    return MappingResponse(mapping);
                }

                var created = new ProductManufacturer();
                delta.Merge(created);
                created.Id = 0;
                await _manufacturerService.InsertProductManufacturerAsync(created);
                onCommit.Add(async () => await CustomerActivityService.InsertActivityAsync("AddNewProductManufacturerMapping",
                    await LocalizationService.GetResourceAsync("ActivityLog.AddNewProductManufacturerMapping"), created));
                return MappingResponse(created);
            }, HttpContext.RequestAborted);
        }

        [HttpPut]
        [Route("/api/product_manufacturer_mappings/{id}", Name = "UpdateProductManufacturerMapping")]
        [ProducesResponseType(typeof(ProductManufacturerMappingsRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.Conflict)]
        [ProducesResponseType(typeof(ErrorsRootObject), 422)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.ServiceUnavailable)]
        public async Task<IActionResult> UpdateProductManufacturerMapping(
            [FromBody]
            [ModelBinder(typeof(JsonModelBinder<ProductManufacturerMappingsDto>))]
            Delta<ProductManufacturerMappingsDto> delta)
        {
            if (!ModelState.IsValid)
                return Error();

            if ((delta.HasProperty(nameof(ProductManufacturerMappingsDto.ProductId)) && delta.Dto.ProductId is not > 0) ||
                (delta.HasProperty(nameof(ProductManufacturerMappingsDto.ManufacturerId)) && delta.Dto.ManufacturerId is not > 0))
                return Error(HttpStatusCode.BadRequest, "product_manufacturer_mapping", "mapping IDs must be positive");

            return await _writes.ExecuteAsync(async onCommit =>
            {
                var mapping = await _writes.GetByIdAsync(delta.Dto.Id);
                if (mapping == null)
                    return Error(HttpStatusCode.NotFound, "product_manufacturer_mapping", "not found");

                var scopeError = await CheckMappingScopeAsync(mapping.ProductId, mapping.ManufacturerId);
                if (scopeError != null)
                    return scopeError;

                var productId = delta.Dto.ProductId ?? mapping.ProductId;
                var manufacturerId = delta.Dto.ManufacturerId ?? mapping.ManufacturerId;
                if (productId != mapping.ProductId || manufacturerId != mapping.ManufacturerId)
                {
                    scopeError = await CheckMappingScopeAsync(productId, manufacturerId);
                    if (scopeError != null)
                        return scopeError;

                    if ((await FindMappingsAsync(productId, manufacturerId)).Count != 0)
                        return Error(HttpStatusCode.Conflict, "product_manufacturer_mapping", "destination mapping already exists");
                }

                if (delta.MergeScalarChanges(mapping))
                {
                    await _manufacturerService.UpdateProductManufacturerAsync(mapping);
                    onCommit.Add(async () => await CustomerActivityService.InsertActivityAsync("UpdateProdutManufacturerMapping",
                        await LocalizationService.GetResourceAsync("ActivityLog.UpdateProdutManufacturerMapping"), mapping));
                }

                return MappingResponse(mapping);
            }, HttpContext.RequestAborted);
        }

        [HttpDelete]
        [Route("/api/product_manufacturer_mappings/{id}", Name = "DeleteProductManufacturerMapping")]
        [ProducesResponseType(typeof(void), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [GetRequestsErrorInterceptorActionFilter]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.ServiceUnavailable)]
        public async Task<IActionResult> DeleteProductManufacturerMapping([FromRoute] int id)
        {
            if (id <= 0)
            {
                return Error(HttpStatusCode.BadRequest, "id", "invalid id");
            }

            return await _writes.ExecuteAsync(async onCommit =>
            {
                var mapping = await _writes.GetByIdAsync(id);
                if (mapping == null)
                    return Error(HttpStatusCode.NotFound, "product_manufacturer_mapping", "not found");

                var scopeError = await CheckMappingScopeAsync(mapping.ProductId, mapping.ManufacturerId);
                if (scopeError != null)
                    return scopeError;

                await _manufacturerService.DeleteProductManufacturerAsync(mapping);
                onCommit.Add(async () => await CustomerActivityService.InsertActivityAsync("DeleteProductManufacturerMapping",
                    await LocalizationService.GetResourceAsync("ActivityLog.DeleteProductManufacturerMapping"), mapping));
                return new RawJsonActionResult("{}");
            }, HttpContext.RequestAborted);
        }

        private async Task<IActionResult> CheckMappingScopeAsync(int productId, int manufacturerId)
        {
            if (!await _scope.IsInScopeAsync(await _manufacturerApiService.GetManufacturerByIdAsync(manufacturerId)))
                return Error(HttpStatusCode.NotFound, "manufacturer_id", "not found");

            if (!await _scope.CanWriteProductAsync(productId))
                return Error(HttpStatusCode.NotFound, "product_id", "not found");

            return null;
        }

        private Task<IList<ProductManufacturer>> FindMappingsAsync(int productId, int manufacturerId) =>
            _productManufacturerMappingsService.GetMappingsAsync(productId, manufacturerId, limit: 2);

        private IActionResult MappingResponse(ProductManufacturer mapping)
        {
            var result = new ProductManufacturerMappingsRootObject();
            result.ProductManufacturerMappingsDtos.Add(mapping.ToDto());
            return new RawJsonActionResult(JsonFieldsSerializer.Serialize(result, string.Empty));
        }
    }
}
