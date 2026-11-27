using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Api.Attributes;
using Nop.Plugin.Api.Authorization.Attributes;
using Nop.Plugin.Api.Delta;
using Nop.Plugin.Api.DTO.Errors;
using Nop.Plugin.Api.DTO.ProductCategoryMappings;
using Nop.Plugin.Api.Infrastructure;
using Nop.Plugin.Api.JSON.ActionResults;
using Nop.Plugin.Api.JSON.Serializers;
using Nop.Plugin.Api.MappingExtensions;
using Nop.Plugin.Api.ModelBinders;
using Nop.Plugin.Api.Models.ProductCategoryMappingsParameters;
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
    [AuthorizePermission(StandardPermission.Catalog.CATEGORIES_CREATE_EDIT_DELETE)]
    public class ProductCategoryMappingsController : BaseApiController
    {
        private readonly RelationshipWrites<ProductCategory> _writes;
        private readonly RelationshipWriteScope _scope;
        private readonly ICategoryService _categoryService;
        private readonly ICategoryApiService _categoryApiService;
        private readonly IProductCategoryMappingsApiService _productCategoryMappingsService;

        public ProductCategoryMappingsController(
            IProductCategoryMappingsApiService productCategoryMappingsService,
            ICategoryService categoryService,
            IJsonFieldsSerializer jsonFieldsSerializer,
            IAclService aclService,
            ICustomerService customerService,
            IStoreMappingService storeMappingService,
            IStoreService storeService,
            IDiscountService discountService,
            ICustomerActivityService customerActivityService,
            ILocalizationService localizationService,
            ICategoryApiService categoryApiService,
            IPictureService pictureService,
            RelationshipWrites<ProductCategory> writes,
            RelationshipWriteScope scope)
            : base(jsonFieldsSerializer, aclService, customerService, storeMappingService, storeService, discountService, customerActivityService,
                   localizationService, pictureService)
        {
            _productCategoryMappingsService = productCategoryMappingsService;
            _categoryApiService = categoryApiService;
            _categoryService = categoryService;
            _writes = writes;
            _scope = scope;
        }

        /// <summary>
        ///     Receive a list of all Product-Category mappings
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="400">Bad Request</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/product_category_mappings", Name = "GetProductCategoryMappings")]
        [ProducesResponseType(typeof(ProductCategoryMappingsRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [GetRequestsErrorInterceptorActionFilter]
        [UnpagedCollection]
        public async Task<IActionResult> GetMappings([FromQuery] ProductCategoryMappingsParametersModel parameters)
        {
            if (parameters.Limit < Constants.Configurations.MinLimit || parameters.Limit > Constants.Configurations.MaxLimit)
            {
                return Error(HttpStatusCode.BadRequest, "limit", "invalid limit parameter");
            }

            if (parameters.Page < Constants.Configurations.DefaultPageValue)
            {
                return Error(HttpStatusCode.BadRequest, "page", "invalid page parameter");
            }

            IList<ProductCategoryMappingDto> mappingsAsDtos =
                (await _productCategoryMappingsService.GetMappingsAsync(parameters.ProductId,
                                                            parameters.CategoryId,
                                                            GetCollectionLimit(parameters.Limit),
                                                            parameters.Page,
                                                            parameters.SinceId)).Select(x => x.ToDto()).ToList();

            var productCategoryMappingRootObject = new ProductCategoryMappingsRootObject
            {
                ProductCategoryMappingDtos = mappingsAsDtos
            };

            var json = JsonFieldsSerializer.Serialize(productCategoryMappingRootObject, parameters.Fields);

            return new RawJsonActionResult(json);
        }

        /// <summary>
        ///     Receive a count of all Product-Category mappings
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/product_category_mappings/count", Name = "GetProductCategoryMappingsCount")]
        [ProducesResponseType(typeof(ProductCategoryMappingsCountRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [GetRequestsErrorInterceptorActionFilter]
        public async Task<IActionResult> GetMappingsCount([FromQuery] ProductCategoryMappingsCountParametersModel parameters)
        {
            if (parameters.ProductId < 0)
            {
                return Error(HttpStatusCode.BadRequest, "product_id", "invalid product_id");
            }

            if (parameters.CategoryId < 0)
            {
                return Error(HttpStatusCode.BadRequest, "category_id", "invalid category_id");
            }

            var mappingsCount = await _productCategoryMappingsService.GetMappingsCountAsync(parameters.ProductId,
                                                                                 parameters.CategoryId);

            var productCategoryMappingsCountRootObject = new ProductCategoryMappingsCountRootObject
            {
                Count = mappingsCount
            };

            return Ok(productCategoryMappingsCountRootObject);
        }

        /// <summary>
        ///     Retrieve Product-Category mappings by spcified id
        /// </summary>
        /// ///
        /// <param name="id">Id of the Product-Category mapping</param>
        /// <param name="fields">Fields from the Product-Category mapping you want your json to contain</param>
        /// <response code="200">OK</response>
        /// <response code="404">Not Found</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/product_category_mappings/{id}", Name = "GetProductCategoryMappingById")]
        [ProducesResponseType(typeof(ProductCategoryMappingsRootObject), (int)HttpStatusCode.OK)]
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

            var mapping = await _productCategoryMappingsService.GetByIdAsync(id);

            if (mapping == null)
            {
                return Error(HttpStatusCode.NotFound, "product_category_mapping", "not found");
            }

            var productCategoryMappingsRootObject = new ProductCategoryMappingsRootObject();
            productCategoryMappingsRootObject.ProductCategoryMappingDtos.Add(mapping.ToDto());

            var json = JsonFieldsSerializer.Serialize(productCategoryMappingsRootObject, fields);

            return new RawJsonActionResult(json);
        }

        [HttpPost]
        [Route("/api/product_category_mappings", Name = "CreateProductCategoryMapping")]
        [ProducesResponseType(typeof(ProductCategoryMappingsRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.Conflict)]
        [ProducesResponseType(typeof(ErrorsRootObject), 422)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.ServiceUnavailable)]
        public async Task<IActionResult> CreateProductCategoryMapping(
            [FromBody]
            [ModelBinder(typeof(JsonModelBinder<ProductCategoryMappingDto>))]
            Delta<ProductCategoryMappingDto> delta)
        {
            if (!ModelState.IsValid)
                return Error();

            var dto = delta.Dto;
            if (dto.ProductId is not > 0 || dto.CategoryId is not > 0)
                return Error(HttpStatusCode.BadRequest, "product_category_mapping", "positive product_id and category_id are required");

            return await _writes.ExecuteAsync(async onCommit =>
            {
                var scopeError = await CheckMappingScopeAsync(dto.ProductId.Value, dto.CategoryId.Value);
                if (scopeError != null)
                    return scopeError;

                var existing = await FindMappingsAsync(dto.ProductId.Value, dto.CategoryId.Value);
                if (existing.Count != 0)
                {
                    var mapping = existing[0];
                    if (existing.Count > 1 ||
                        dto.IsFeaturedProduct.HasValue && dto.IsFeaturedProduct.Value != mapping.IsFeaturedProduct ||
                        dto.DisplayOrder.HasValue && dto.DisplayOrder.Value != mapping.DisplayOrder)
                        return Error(HttpStatusCode.Conflict, "product_category_mapping", "existing mapping conflicts with the request");

                    return MappingResponse(mapping);
                }

                var created = new ProductCategory();
                delta.Merge(created);
                created.Id = 0;
                await _categoryService.InsertProductCategoryAsync(created);
                onCommit.Add(async () => await CustomerActivityService.InsertActivityAsync("AddNewProductCategoryMapping",
                    await LocalizationService.GetResourceAsync("ActivityLog.AddNewProductCategoryMapping"), created));
                return MappingResponse(created);
            }, HttpContext.RequestAborted);
        }

        [HttpPut]
        [Route("/api/product_category_mappings/{id}", Name = "UpdateProductCategoryMapping")]
        [ProducesResponseType(typeof(ProductCategoryMappingsRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.Conflict)]
        [ProducesResponseType(typeof(ErrorsRootObject), 422)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.ServiceUnavailable)]
        public async Task<IActionResult> UpdateProductCategoryMapping(
            [FromBody]
            [ModelBinder(typeof(JsonModelBinder<ProductCategoryMappingDto>))]
            Delta<ProductCategoryMappingDto> delta)
        {
            if (!ModelState.IsValid)
                return Error();

            if ((delta.HasProperty(nameof(ProductCategoryMappingDto.ProductId)) && delta.Dto.ProductId is not > 0) ||
                (delta.HasProperty(nameof(ProductCategoryMappingDto.CategoryId)) && delta.Dto.CategoryId is not > 0))
                return Error(HttpStatusCode.BadRequest, "product_category_mapping", "mapping IDs must be positive");

            return await _writes.ExecuteAsync(async onCommit =>
            {
                var mapping = await _writes.GetByIdAsync(delta.Dto.Id);
                if (mapping == null)
                    return Error(HttpStatusCode.NotFound, "product_category_mapping", "not found");

                var scopeError = await CheckMappingScopeAsync(mapping.ProductId, mapping.CategoryId);
                if (scopeError != null)
                    return scopeError;

                var productId = delta.Dto.ProductId ?? mapping.ProductId;
                var categoryId = delta.Dto.CategoryId ?? mapping.CategoryId;
                if (productId != mapping.ProductId || categoryId != mapping.CategoryId)
                {
                    scopeError = await CheckMappingScopeAsync(productId, categoryId);
                    if (scopeError != null)
                        return scopeError;

                    if ((await FindMappingsAsync(productId, categoryId)).Count != 0)
                        return Error(HttpStatusCode.Conflict, "product_category_mapping", "destination mapping already exists");
                }

                if (delta.MergeScalarChanges(mapping))
                {
                    await _categoryService.UpdateProductCategoryAsync(mapping);
                    onCommit.Add(async () => await CustomerActivityService.InsertActivityAsync("UpdateProdutCategoryMapping",
                        await LocalizationService.GetResourceAsync("ActivityLog.UpdateProdutCategoryMapping"), mapping));
                }

                return MappingResponse(mapping);
            }, HttpContext.RequestAborted);
        }

        [HttpDelete]
        [Route("/api/product_category_mappings/{id}", Name = "DeleteProductCategoryMapping")]
        [ProducesResponseType(typeof(void), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [GetRequestsErrorInterceptorActionFilter]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.ServiceUnavailable)]
        public async Task<IActionResult> DeleteProductCategoryMapping([FromRoute] int id)
        {
            if (id <= 0)
            {
                return Error(HttpStatusCode.BadRequest, "id", "invalid id");
            }

            return await _writes.ExecuteAsync(async onCommit =>
            {
                var mapping = await _writes.GetByIdAsync(id);
                if (mapping == null)
                    return Error(HttpStatusCode.NotFound, "product_category_mapping", "not found");

                var scopeError = await CheckMappingScopeAsync(mapping.ProductId, mapping.CategoryId);
                if (scopeError != null)
                    return scopeError;

                await _categoryService.DeleteProductCategoryAsync(mapping);
                onCommit.Add(async () => await CustomerActivityService.InsertActivityAsync("DeleteProductCategoryMapping",
                    await LocalizationService.GetResourceAsync("ActivityLog.DeleteProductCategoryMapping"), mapping));
                return new RawJsonActionResult("{}");
            }, HttpContext.RequestAborted);
        }

        private async Task<IActionResult> CheckMappingScopeAsync(int productId, int categoryId)
        {
            if (!await _scope.IsInScopeAsync(await _categoryApiService.GetCategoryByIdAsync(categoryId)))
                return Error(HttpStatusCode.NotFound, "category_id", "not found");

            if (!await _scope.CanWriteProductAsync(productId))
                return Error(HttpStatusCode.NotFound, "product_id", "not found");

            return null;
        }

        private Task<IList<ProductCategory>> FindMappingsAsync(int productId, int categoryId) =>
            _productCategoryMappingsService.GetMappingsAsync(productId, categoryId, limit: 2);

        private IActionResult MappingResponse(ProductCategory mapping)
        {
            var result = new ProductCategoryMappingsRootObject();
            result.ProductCategoryMappingDtos.Add(mapping.ToDto());
            return new RawJsonActionResult(JsonFieldsSerializer.Serialize(result, string.Empty));
        }
    }
}
