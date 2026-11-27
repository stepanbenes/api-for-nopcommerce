using LinqToDB.Async;
using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Catalog;
using Nop.Data;
using Nop.Plugin.Api.Attributes;
using Nop.Plugin.Api.Authorization.Attributes;
using Nop.Plugin.Api.Delta;
using Nop.Plugin.Api.DTO.Errors;
using Nop.Plugin.Api.DTO.SpecificationAttributes;
using Nop.Plugin.Api.Helpers;
using Nop.Plugin.Api.Infrastructure;
using Nop.Plugin.Api.JSON.ActionResults;
using Nop.Plugin.Api.JSON.Serializers;
using Nop.Plugin.Api.ModelBinders;
using Nop.Plugin.Api.Models.ProductSpecificationAttributesParameters;
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
    [AuthorizePermission(StandardPermission.Catalog.SPECIFICATION_ATTRIBUTES_CREATE_EDIT_DELETE)]
    public class ProductSpecificationAttributesController : BaseApiController
    {
        private readonly RelationshipWrites<ProductSpecificationAttribute> _writes;
        private readonly RelationshipWriteScope _scope;
        private readonly IRepository<SpecificationAttributeOption> _optionRepository;
        private readonly IDTOHelper _dtoHelper;
        private readonly ISpecificationAttributeApiService _specificationAttributeApiService;
        private readonly ISpecificationAttributeService _specificationAttributeService;

        public ProductSpecificationAttributesController(
            IJsonFieldsSerializer jsonFieldsSerializer,
            ICustomerActivityService customerActivityService,
            ILocalizationService localizationService,
            IAclService aclService,
            IStoreMappingService storeMappingService,
            IStoreService storeService,
            ICustomerService customerService,
            IDiscountService discountService,
            IPictureService pictureService,
            ISpecificationAttributeService specificationAttributeService,
            ISpecificationAttributeApiService specificationAttributesApiService,
            IDTOHelper dtoHelper,
            RelationshipWrites<ProductSpecificationAttribute> writes,
            RelationshipWriteScope scope,
            IRepository<SpecificationAttributeOption> optionRepository) : base(jsonFieldsSerializer, aclService, customerService, storeMappingService, storeService, discountService,
                                         customerActivityService, localizationService, pictureService)
        {
            _specificationAttributeService = specificationAttributeService;
            _specificationAttributeApiService = specificationAttributesApiService;
            _dtoHelper = dtoHelper;
            _writes = writes;
            _scope = scope;
            _optionRepository = optionRepository;
        }

        /// <summary>
        ///     Receive a list of all product specification attributes
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="400">Bad Request</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/productspecificationattributes", Name = "GetProductSpecificationAttributes")]
        [ProducesResponseType(typeof(ProductSpecificationAttributesRootObjectDto), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [GetRequestsErrorInterceptorActionFilter]
        [UnpagedCollection]
        public async Task<IActionResult> GetProductSpecificationAttributes([FromQuery] ProductSpecificationAttributesParametersModel parameters)
        {
            if (parameters.Limit < Constants.Configurations.MinLimit || parameters.Limit > Constants.Configurations.MaxLimit)
            {
                return Error(HttpStatusCode.BadRequest, "limit", "invalid limit parameter");
            }

            if (parameters.Page < Constants.Configurations.DefaultPageValue)
            {
                return Error(HttpStatusCode.BadRequest, "page", "invalid page parameter");
            }

            var productSpecificationAttribtues =
                await _specificationAttributeApiService.GetProductSpecificationAttributesAsync(parameters.ProductId, parameters.SpecificationAttributeOptionId,
                                                                                    parameters.AllowFiltering, parameters.ShowOnProductPage, GetCollectionLimit(parameters.Limit),
                                                                                    parameters.Page, parameters.SinceId);

            var productSpecificationAttributeDtos = productSpecificationAttribtues.Select(x => _dtoHelper.PrepareProductSpecificationAttributeDto(x)).ToList();

            var productSpecificationAttributesRootObject = new ProductSpecificationAttributesRootObjectDto
            {
                ProductSpecificationAttributes = productSpecificationAttributeDtos
            };

            var json = JsonFieldsSerializer.Serialize(productSpecificationAttributesRootObject, parameters.Fields);

            return new RawJsonActionResult(json);
        }

        /// <summary>
        ///     Receive a count of all product specification attributes
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/productspecificationattributes/count", Name = "GetProductSpecificationAttributesCount")]
        [ProducesResponseType(typeof(ProductSpecificationAttributesCountRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [GetRequestsErrorInterceptorActionFilter]
        public async Task<IActionResult> GetProductSpecificationAttributesCount([FromQuery] ProductSpecificationAttributesCountParametersModel parameters)
        {
            var productSpecificationAttributesCount = await _specificationAttributeService.GetProductSpecificationAttributeCountAsync(parameters.ProductId, parameters.SpecificationAttributeOptionId);

            var productSpecificationAttributesCountRootObject = new ProductSpecificationAttributesCountRootObject
            {
                Count = productSpecificationAttributesCount
            };

            return Ok(productSpecificationAttributesCountRootObject);
        }

        /// <summary>
        ///     Retrieve product specification attribute by spcified id
        /// </summary>
        /// <param name="id">Id of the product specification  attribute</param>
        /// <param name="fields">Fields from the product specification attribute you want your json to contain</param>
        /// <response code="200">OK</response>
        /// <response code="404">Not Found</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/productspecificationattributes/{id}", Name = "GetProductSpecificationAttributeById")]
        [ProducesResponseType(typeof(ProductSpecificationAttributesRootObjectDto), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [GetRequestsErrorInterceptorActionFilter]
        public async Task<IActionResult> GetProductSpecificationAttributeById([FromRoute] int id, [FromQuery] string fields = "")
        {
            if (id <= 0)
            {
                return Error(HttpStatusCode.BadRequest, "id", "invalid id");
            }

            var productSpecificationAttribute = await _specificationAttributeService.GetProductSpecificationAttributeByIdAsync(id);

            if (productSpecificationAttribute == null)
            {
                return Error(HttpStatusCode.NotFound, "product specification attribute", "not found");
            }

            var productSpecificationAttributeDto = _dtoHelper.PrepareProductSpecificationAttributeDto(productSpecificationAttribute);

            var productSpecificationAttributesRootObject = new ProductSpecificationAttributesRootObjectDto();
            productSpecificationAttributesRootObject.ProductSpecificationAttributes.Add(productSpecificationAttributeDto);

            var json = JsonFieldsSerializer.Serialize(productSpecificationAttributesRootObject, fields);

            return new RawJsonActionResult(json);
        }

        [HttpPost]
        [Route("/api/productspecificationattributes", Name = "CreateProductSpecificationAttribute")]
        [ProducesResponseType(typeof(ProductSpecificationAttributesRootObjectDto), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.Conflict)]
        [ProducesResponseType(typeof(ErrorsRootObject), 422)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.ServiceUnavailable)]
        public async Task<IActionResult> CreateProductSpecificationAttribute(
            [FromBody]
            [ModelBinder(typeof(JsonModelBinder<ProductSpecificationAttributeDto>))]
            Delta<ProductSpecificationAttributeDto> delta)
        {
            if (!ModelState.IsValid)
                return Error();

            return await _writes.ExecuteAsync(async onCommit =>
            {
                var requested = new ProductSpecificationAttribute();
                delta.Merge(requested);
                requested.Id = 0;
                var error = await ValidateMappingAsync(requested);
                if (error != null)
                    return error;

                var existing = await FindMappingsAsync(requested);
                if (existing.Count != 0)
                {
                    var mapping = existing[0];
                    if (existing.Count > 1 ||
                        delta.HasProperty(nameof(ProductSpecificationAttributeDto.CustomValue)) && mapping.CustomValue != requested.CustomValue ||
                        delta.HasProperty(nameof(ProductSpecificationAttributeDto.AllowFiltering)) && mapping.AllowFiltering != requested.AllowFiltering ||
                        delta.HasProperty(nameof(ProductSpecificationAttributeDto.ShowOnProductPage)) && mapping.ShowOnProductPage != requested.ShowOnProductPage ||
                        delta.HasProperty(nameof(ProductSpecificationAttributeDto.DisplayOrder)) && mapping.DisplayOrder != requested.DisplayOrder)
                        return Error(HttpStatusCode.Conflict, "product_specification_attribute", "existing mapping conflicts with the request");

                    return MappingResponse(mapping);
                }

                await _specificationAttributeService.InsertProductSpecificationAttributeAsync(requested);
                onCommit.Add(() => CustomerActivityService.InsertActivityAsync("AddNewProductSpecificationAttribute", requested.Id.ToString()));
                return MappingResponse(requested);
            }, HttpContext.RequestAborted);
        }

        [HttpPut]
        [Route("/api/productspecificationattributes/{id}", Name = "UpdateProductSpecificationAttribute")]
        [ProducesResponseType(typeof(ProductSpecificationAttributesRootObjectDto), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.Conflict)]
        [ProducesResponseType(typeof(ErrorsRootObject), 422)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.ServiceUnavailable)]
        public async Task<IActionResult> UpdateProductSpecificationAttribute(
            [FromBody]
            [ModelBinder(typeof(JsonModelBinder<ProductSpecificationAttributeDto>))]
            Delta<ProductSpecificationAttributeDto> delta)
        {
            if (!ModelState.IsValid)
                return Error();

            return await _writes.ExecuteAsync(async onCommit =>
            {
                var mapping = await _writes.GetByIdAsync(delta.Dto.Id);
                if (mapping == null || !await _scope.CanWriteProductAsync(mapping.ProductId))
                    return Error(HttpStatusCode.NotFound, "product_specification_attribute", "not found");

                if (!delta.MergeScalarChanges(mapping))
                    return MappingResponse(mapping);

                var error = await ValidateMappingAsync(mapping);
                if (error != null)
                    return error;

                if ((await FindMappingsAsync(mapping)).Any(existing => existing.Id != mapping.Id))
                    return Error(HttpStatusCode.Conflict, "product_specification_attribute", "destination mapping already exists");

                await _specificationAttributeService.UpdateProductSpecificationAttributeAsync(mapping);
                onCommit.Add(() => CustomerActivityService.InsertActivityAsync("EditProductSpecificationAttribute", mapping.Id.ToString()));
                return MappingResponse(mapping);
            }, HttpContext.RequestAborted);
        }

        [HttpDelete]
        [Route("/api/productspecificationattributes/{id}", Name = "DeleteProductSpecificationAttribute")]
        [ProducesResponseType(typeof(void), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(ErrorsRootObject), 422)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.ServiceUnavailable)]
        public async Task<IActionResult> DeleteProductSpecificationAttribute([FromRoute] int id)
        {
            if (id <= 0)
            {
                return Error(HttpStatusCode.BadRequest, "id", "invalid id");
            }

            return await _writes.ExecuteAsync(async onCommit =>
            {
                var mapping = await _writes.GetByIdAsync(id);
                if (mapping == null || !await _scope.CanWriteProductAsync(mapping.ProductId))
                    return Error(HttpStatusCode.NotFound, "product_specification_attribute", "not found");

                await _specificationAttributeService.DeleteProductSpecificationAttributeAsync(mapping);
                onCommit.Add(async () => await CustomerActivityService.InsertActivityAsync("DeleteProductSpecificationAttribute",
                    await LocalizationService.GetResourceAsync("ActivityLog.DeleteProductSpecificationAttribute"), mapping));
                return new RawJsonActionResult("{}");
            }, HttpContext.RequestAborted);
        }

        private async Task<IActionResult> ValidateMappingAsync(ProductSpecificationAttribute mapping)
        {
            if (mapping.ProductId <= 0 || mapping.SpecificationAttributeOptionId <= 0 ||
                !Enum.IsDefined(mapping.AttributeType))
                return Error(HttpStatusCode.BadRequest, "product_specification_attribute", "invalid product, option, or attribute type");

            if (!await _scope.CanWriteProductAsync(mapping.ProductId))
                return Error(HttpStatusCode.NotFound, "product_id", "not found");

            if (await _optionRepository.GetByIdAsync(mapping.SpecificationAttributeOptionId) == null)
                return Error(HttpStatusCode.NotFound, "specification_attribute_option_id", "not found");

            return null;
        }

        private Task<List<ProductSpecificationAttribute>> FindMappingsAsync(ProductSpecificationAttribute requested)
        {
            var query = _writes.Table.Where(mapping => mapping.ProductId == requested.ProductId &&
                mapping.SpecificationAttributeOptionId == requested.SpecificationAttributeOptionId &&
                mapping.AttributeTypeId == requested.AttributeTypeId);
            if (requested.AttributeType != SpecificationAttributeType.Option)
                query = query.Where(mapping => mapping.CustomValue == requested.CustomValue);

            return query.Take(2).ToListAsync();
        }

        private IActionResult MappingResponse(ProductSpecificationAttribute mapping)
        {
            var result = new ProductSpecificationAttributesRootObjectDto();
            result.ProductSpecificationAttributes.Add(_dtoHelper.PrepareProductSpecificationAttributeDto(mapping));
            return new RawJsonActionResult(JsonFieldsSerializer.Serialize(result, string.Empty));
        }
    }
}
