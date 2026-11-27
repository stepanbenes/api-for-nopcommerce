using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Api.Attributes;
using Nop.Plugin.Api.Authorization.Attributes;
using Nop.Plugin.Api.Delta;
using Nop.Plugin.Api.DTO.Errors;
using Nop.Plugin.Api.DTO.ProductWarehouseIventories;
using Nop.Plugin.Api.Infrastructure;
using Nop.Plugin.Api.JSON.ActionResults;
using Nop.Plugin.Api.JSON.Serializers;
using Nop.Plugin.Api.MappingExtensions;
using Nop.Plugin.Api.ModelBinders;
using Nop.Plugin.Api.Models.ProductWarehouseInventoryParameters;
using Nop.Plugin.Api.Services;
using Nop.Services.Catalog;
using Nop.Services.Authentication;
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
    public class ProductWarehouseInventoryController : BaseApiController
    {
        private readonly IProductApiService _productApiService;
        private readonly IProductService _productService;
        private readonly IWarehouseApiService _warehouseApiService;
        private readonly IProductWarehouseInventoriesApiService _productWarehouseInventoriesService;

        public ProductWarehouseInventoryController(
            IProductApiService productApiService,
            IProductService productService,
            IWarehouseApiService warehouseApiService,
            IProductWarehouseInventoriesApiService productWarehouseInventoriesService,
            IJsonFieldsSerializer jsonFieldsSerializer,
            IAclService aclService,
            ICustomerService customerService,
            IStoreMappingService storeMappingService,
            IStoreService storeService,
            IDiscountService discountService,
            ICustomerActivityService customerActivityService,
            ILocalizationService localizationService,
            IPictureService pictureService)
            : base(jsonFieldsSerializer, aclService, customerService, storeMappingService, storeService,
                discountService, customerActivityService, localizationService, pictureService)
        {
            _productApiService = productApiService;
            _productService = productService;
            _warehouseApiService = warehouseApiService;
            _productWarehouseInventoriesService = productWarehouseInventoriesService;
        }

        /// <summary>
        ///     Receive a list of all Product-Warehouse inventory
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="400">Bad Request</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/product_warehouse_inventories", Name = "GetProductCategoryInventories")]
        [ApiAudience(ApiAudience.BackOffice)]
        [ProducesResponseType(typeof(ProductWarehouseInventoryRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [GetRequestsErrorInterceptorActionFilter]
        [UnpagedCollection]
        public async Task<IActionResult> GetInventories([FromQuery] ProductWarehouseInventoryParametersModel parameters,
            [FromServices] IAuthenticationService authenticationService, [FromServices] IStoreContext storeContext)
        {
            if (parameters.Limit < Constants.Configurations.MinLimit || parameters.Limit > Constants.Configurations.MaxLimit)
            {
                return Error(HttpStatusCode.BadRequest, "limit", "invalid limit parameter");
            }

            if (parameters.Page < Constants.Configurations.DefaultPageValue)
            {
                return Error(HttpStatusCode.BadRequest, "page", "invalid page parameter");
            }

            var actor = await authenticationService.GetAuthenticatedCustomerAsync();
            if (actor == null)
                return AccessDenied();

            var store = await storeContext.GetCurrentStoreAsync();
            IList<ProductWarehouseInventoryDto> inventoryDtos =
                (await _productWarehouseInventoriesService.GetMappingsAsync(actor, store.Id, parameters.ProductId,
                    parameters.WarehouseId,
                    GetCollectionLimit(parameters.Limit),
                    parameters.Page,
                    parameters.SinceId)).Select(x => x.ToDto()).ToList();

            var productWarehouseInventoryRootObject = new ProductWarehouseInventoryRootObject()
            {
                ProductWarehouseInventoryDtos = inventoryDtos
            };

            var json = JsonFieldsSerializer.Serialize(productWarehouseInventoryRootObject, parameters.Fields);

            return new RawJsonActionResult(json);
        }

        /// <summary>
        ///     Retrieve Product-Warehouse inventories by specified id
        /// </summary>
        /// ///
        /// <param name="id">Id of the Product-Warehouse inventory</param>
        /// <param name="fields">Fields from the Product-Warehouse inventory you want your json to contain</param>
        /// <response code="200">OK</response>
        /// <response code="404">Not Found</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/product_warehouse_inventories/{id}", Name = "GetProductWarehouseInventoriesById")]
        [ApiAudience(ApiAudience.BackOffice)]
        [ProducesResponseType(typeof(ProductWarehouseInventoryRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [GetRequestsErrorInterceptorActionFilter]
        public async Task<IActionResult> GetInventoryById([FromRoute] int id,
            [FromServices] IAuthenticationService authenticationService, [FromServices] IStoreContext storeContext,
            [FromQuery] string fields = "")
        {
            if (id <= 0)
            {
                return Error(HttpStatusCode.BadRequest, "id", "invalid id");
            }

            var actor = await authenticationService.GetAuthenticatedCustomerAsync();
            if (actor == null)
                return AccessDenied();

            var store = await storeContext.GetCurrentStoreAsync();
            var mapping = await _productWarehouseInventoriesService.GetScopedByIdAsync(id, actor, store.Id);

            if (mapping == null)
            {
                return Error(HttpStatusCode.NotFound, "product_warehouse_inventories", "not found");
            }

            var productWarehouseInventoryRootObject = new ProductWarehouseInventoryRootObject();
            productWarehouseInventoryRootObject.ProductWarehouseInventoryDtos.Add(mapping.ToDto());

            var json = JsonFieldsSerializer.Serialize(productWarehouseInventoryRootObject, fields);

            return new RawJsonActionResult(json);
        }

        [HttpPost]
        [Route("/api/product_warehouse_inventories", Name = "CreateProductWarehouseInventory")]
        [ApiAudience(ApiAudience.BackOffice)]
        [ProducesResponseType(typeof(ProductWarehouseInventoryRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(ErrorsRootObject), 422)]
        public async Task<IActionResult> CreateProductWarehouseInventory(
            [FromBody]
            [ModelBinder(typeof(JsonModelBinder<ProductWarehouseInventoryDto>))]
            Delta<ProductWarehouseInventoryDto> productWarehouseInventoryDelta)
        {
            // Here we display the errors if the validation has failed at some point.
            if (!ModelState.IsValid)
            {
                return Error();
            }

            var warehouse = await _warehouseApiService.GetWarehouseByIdAsync(productWarehouseInventoryDelta.Dto.WarehouseId.Value);
            if (warehouse == null)
            {
                return Error(HttpStatusCode.NotFound, "warehouse_id", "not found");
            }

            var product = await _productApiService.GetProductByIdAsync(productWarehouseInventoryDelta.Dto.ProductId.Value);
            if (product == null)
            {
                return Error(HttpStatusCode.NotFound, "product_id", "not found");
            }

            var ivnentoriesCount = await _productWarehouseInventoriesService.GetIvnentoriesCountAsync(product.Id, warehouse.Id);

            if (ivnentoriesCount > 0)
            {
                return Error(HttpStatusCode.BadRequest, "product_warehouse_inventories", "already exist");
            }

            var newProductWarehouseInventory = new ProductWarehouseInventory();
            productWarehouseInventoryDelta.Merge(newProductWarehouseInventory);

            //inserting new category
            await _productService.InsertProductWarehouseInventoryAsync(newProductWarehouseInventory);

            // Preparing the result dto of the new product category mapping
            var newProductWarehouseInventoryDto = newProductWarehouseInventory.ToDto();

            var productWarehouseInventoryRootObject = new ProductWarehouseInventoryRootObject();

            productWarehouseInventoryRootObject.ProductWarehouseInventoryDtos.Add(newProductWarehouseInventoryDto);

            var json = JsonFieldsSerializer.Serialize(productWarehouseInventoryRootObject, string.Empty);

            //activity log 
            await CustomerActivityService.InsertActivityAsync("AddNewProductWarehouseInventory", await LocalizationService.GetResourceAsync("ActivityLog.AddNewProductWarehouseInventory"),
                                                   newProductWarehouseInventory);

            return new RawJsonActionResult(json);
        }

        [HttpPut]
        [Route("/api/product_warehouse_inventories/{id}", Name = "UpdateProductWarehouseInventory")]
        [ApiAudience(ApiAudience.BackOffice)]
        [ProducesResponseType(typeof(ProductWarehouseInventoryRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(ErrorsRootObject), 422)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.Conflict)]
        [ProducesResponseType(typeof(ProblemDetails), (int)HttpStatusCode.ServiceUnavailable)]
        public async Task<IActionResult> UpdateProductWarehouseInventory(
            [FromBody]
            [ModelBinder(typeof(JsonModelBinder<ProductWarehouseInventoryDto>))]
            Delta<ProductWarehouseInventoryDto> productWarehouseInventoryDelta,
            [FromServices] IAuthenticationService authenticationService,
            [FromServices] IStoreContext storeContext,
            [FromServices] DesiredPriceStockService desiredService,
            CancellationToken cancellationToken)
        {
            // Here we display the errors if the validation has failed at some point.
            if (!ModelState.IsValid)
            {
                return Error();
            }

            var dto = productWarehouseInventoryDelta.Dto;
            if (productWarehouseInventoryDelta.HasProperty(nameof(dto.ExpectedStockQuantity)))
            {
                if (dto.ExpectedStockQuantity is not { } expectedStock || dto.StockQuantity is not >= 0 ||
                    productWarehouseInventoryDelta.HasProperty(nameof(dto.ProductId)) ||
                    productWarehouseInventoryDelta.HasProperty(nameof(dto.WarehouseId)) ||
                    productWarehouseInventoryDelta.HasProperty(nameof(dto.ReservedQuantity)))
                    return Error(HttpStatusCode.BadRequest, "stock_quantity",
                        "Supply stock_quantity with expected_stock_quantity. Product, warehouse and reserved quantity cannot be set in this request.");

                var actor = await authenticationService.GetAuthenticatedCustomerAsync();
                if (actor == null)
                    return AccessDenied();

                var store = await storeContext.GetCurrentStoreAsync();
                try
                {
                    var (status, inventory) = await desiredService.UpdateWarehouseAsync(actor, store.Id, dto.Id,
                        expectedStock, dto.StockQuantity.Value, cancellationToken);
                    return status switch
                    {
                        "not_found" => Error(HttpStatusCode.NotFound, "product_warehouse_inventory", "not found"),
                        "conflict" => Error(HttpStatusCode.Conflict, "expected_stock_quantity", "Stock no longer matches the expected quantity."),
                        "unsupported_stock_mode" or "below_reserved_quantity" or "stock_adjustment_out_of_range"
                            => Error(HttpStatusCode.UnprocessableEntity, "stock_quantity", status),
                        _ => SerializeInventory(inventory)
                    };
                }
                catch (BulkPriceStockBusyException)
                {
                    return Problem(statusCode: (int)HttpStatusCode.ServiceUnavailable);
                }
            }

            if (productWarehouseInventoryDelta.Dto.WarehouseId.HasValue)
            {
                var warehouse = await _warehouseApiService.GetWarehouseByIdAsync(productWarehouseInventoryDelta.Dto.WarehouseId.Value);
                if (warehouse == null)
                {
                    return Error(HttpStatusCode.NotFound, "warehouse_id", "not found");
                }
            }

            if (productWarehouseInventoryDelta.Dto.ProductId.HasValue)
            {
                var product = await _productApiService.GetProductByIdAsync(productWarehouseInventoryDelta.Dto.ProductId.Value);
                if (product == null)
                {
                    return Error(HttpStatusCode.NotFound, "product_id", "not found");
                }
            }

            // We do not need to validate the category id, because this will happen in the model binder using the dto validator.
            var updateProductWarehouseInventoryId = productWarehouseInventoryDelta.Dto.Id;

            var productWarehouseInventoryEntityToUpdate =
                await _productWarehouseInventoriesService.GetByIdAsync(updateProductWarehouseInventoryId);

            if (productWarehouseInventoryEntityToUpdate == null)
            {
                return Error(HttpStatusCode.NotFound, "product_warehouse_inventory", "not found");
            }

            productWarehouseInventoryDelta.Merge(productWarehouseInventoryEntityToUpdate);

            await _productService.UpdateProductWarehouseInventoryAsync(productWarehouseInventoryEntityToUpdate);

            //activity log
            await CustomerActivityService.InsertActivityAsync("UpdateProductWarehouseInventory", await LocalizationService.GetResourceAsync("ActivityLog.UpdateProductWarehouseInventory"), productWarehouseInventoryEntityToUpdate);

            return SerializeInventory(productWarehouseInventoryEntityToUpdate);
        }

        private IActionResult SerializeInventory(ProductWarehouseInventory inventory)
        {
            var root = new ProductWarehouseInventoryRootObject();
            root.ProductWarehouseInventoryDtos.Add(inventory.ToDto());
            return new RawJsonActionResult(JsonFieldsSerializer.Serialize(root, string.Empty));
        }

        [HttpDelete]
        [Route("/api/product_warehouse_inventories/{id}", Name = "DeleteProductWarehouseInventory")]
        [ApiAudience(ApiAudience.BackOffice)]
        [ProducesResponseType(typeof(void), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [GetRequestsErrorInterceptorActionFilter]
        public async Task<IActionResult> DeleteProductWarehouseInventory([FromRoute] int id)
        {
            if (id <= 0)
            {
                return Error(HttpStatusCode.BadRequest, "id", "invalid id");
            }

            var productWarehouseInventory = await _productWarehouseInventoriesService.GetByIdAsync(id);

            if (productWarehouseInventory == null)
            {
                return Error(HttpStatusCode.NotFound, "product_warehouse_inventory", "not found");
            }

            await _productService.DeleteProductWarehouseInventoryAsync(productWarehouseInventory);

            //activity log 
            await CustomerActivityService.InsertActivityAsync("DeleteProductWarehouseInventory", await LocalizationService.GetResourceAsync("ActivityLog.DeleteProductWarehouseInventory"), productWarehouseInventory);

            return new RawJsonActionResult("{}");
        }
    }
}