using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Api.Attributes;
using Nop.Plugin.Api.Delta;
using Nop.Plugin.Api.DTO.Errors;
using Nop.Plugin.Api.DTO.ShoppingCarts;
using Nop.Plugin.Api.Factories;
using Nop.Plugin.Api.Helpers;
using Nop.Plugin.Api.Infrastructure;
using Nop.Plugin.Api.JSON.ActionResults;
using Nop.Plugin.Api.JSON.Serializers;
using Nop.Plugin.Api.ModelBinders;
using Nop.Plugin.Api.Models.ShoppingCartsParameters;
using Nop.Plugin.Api.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Discounts;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Media;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Services.Stores;

namespace Nop.Plugin.Api.Controllers
{
    public class ShoppingCartItemsController : BaseApiController
    {
        private readonly IDTOHelper _dtoHelper;
        private readonly IFactory<ShoppingCartItem> _factory;
        private readonly IProductAttributeConverter _productAttributeConverter;
        private readonly IProductService _productService;
        private readonly IShoppingCartItemApiService _shoppingCartItemApiService;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly IStoreContext _storeContext;

        public ShoppingCartItemsController(
            IShoppingCartItemApiService shoppingCartItemApiService,
            IJsonFieldsSerializer jsonFieldsSerializer,
            IAclService aclService,
            ICustomerService customerService,
            IStoreMappingService storeMappingService,
            IStoreService storeService,
            IDiscountService discountService,
            ICustomerActivityService customerActivityService,
            ILocalizationService localizationService,
            IShoppingCartService shoppingCartService,
            IProductService productService,
            IFactory<ShoppingCartItem> factory,
            IPictureService pictureService,
            IProductAttributeConverter productAttributeConverter,
            IDTOHelper dtoHelper,
            IStoreContext storeContext)
            : base(jsonFieldsSerializer,
                   aclService,
                   customerService,
                   storeMappingService,
                   storeService,
                   discountService,
                   customerActivityService,
                   localizationService,
                   pictureService)
        {
            _shoppingCartItemApiService = shoppingCartItemApiService;
            _shoppingCartService = shoppingCartService;
            _productService = productService;
            _factory = factory;
            _productAttributeConverter = productAttributeConverter;
            _dtoHelper = dtoHelper;
            _storeContext = storeContext;
        }

        /// <summary>
        ///     Receive a list of all shopping cart items
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="400">Bad Request</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/shopping_cart_items", Name = "GetShoppingCartItems")]
        [ProducesResponseType(typeof(ShoppingCartItemsRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [GetRequestsErrorInterceptorActionFilter]
        public async Task<IActionResult> GetShoppingCartItems(ShoppingCartItemsParametersModel parameters)
        {
            if (parameters.Limit < Constants.Configurations.MinLimit || parameters.Limit > Constants.Configurations.MaxLimit)
            {
                return Error(HttpStatusCode.BadRequest, "limit", "invalid limit parameter");
            }

            if (parameters.Page < Constants.Configurations.DefaultPageValue)
            {
                return Error(HttpStatusCode.BadRequest, "page", "invalid page parameter");
            }

            IList<ShoppingCartItem> shoppingCartItems = _shoppingCartItemApiService.GetShoppingCartItems(null,
                                                                                                         parameters.CreatedAtMin,
                                                                                                         parameters.CreatedAtMax,
                                                                                                         parameters.UpdatedAtMin,
                                                                                                         parameters.UpdatedAtMax,
                                                                                                         parameters.Limit,
                                                                                                         parameters.Page);

            var shoppingCartItemsDtos = await shoppingCartItems.SelectAwait(async shoppingCartItem => await _dtoHelper.PrepareShoppingCartItemDTOAsync(shoppingCartItem)).ToListAsync();

            var shoppingCartsRootObject = new ShoppingCartItemsRootObject
            {
                ShoppingCartItems = shoppingCartItemsDtos
            };

            var json = JsonFieldsSerializer.Serialize(shoppingCartsRootObject, parameters.Fields);

            return new RawJsonActionResult(json);
        }

        /// <summary>
        ///     Receive a list of all shopping cart items by customer id
        /// </summary>
        /// <param name="customerId">Id of the customer whoes shopping cart items you want to get</param>
        /// <param name="parameters"></param>
        /// <response code="200">OK</response>
        /// <response code="400">Bad Request</response>
        /// <response code="404">Not Found</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/shopping_cart_items/{customerId}", Name = "GetShoppingCartItemsByCustomerId")]
        [ProducesResponseType(typeof(ShoppingCartItemsRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [GetRequestsErrorInterceptorActionFilter]
        public async Task<IActionResult> GetShoppingCartItemsByCustomerId(int customerId, ShoppingCartItemsForCustomerParametersModel parameters)
        {
            if (customerId <= Constants.Configurations.DefaultCustomerId)
            {
                return Error(HttpStatusCode.BadRequest, "customer_id", "invalid customer_id");
            }

            if (parameters.Limit < Constants.Configurations.MinLimit || parameters.Limit > Constants.Configurations.MaxLimit)
            {
                return Error(HttpStatusCode.BadRequest, "limit", "invalid limit parameter");
            }

            if (parameters.Page < Constants.Configurations.DefaultPageValue)
            {
                return Error(HttpStatusCode.BadRequest, "page", "invalid page parameter");
            }

            IList<ShoppingCartItem> shoppingCartItems = _shoppingCartItemApiService.GetShoppingCartItems(customerId,
                                                                                                         parameters.CreatedAtMin,
                                                                                                         parameters.CreatedAtMax, parameters.UpdatedAtMin,
                                                                                                         parameters.UpdatedAtMax, parameters.Limit,
                                                                                                         parameters.Page);

            if (shoppingCartItems == null)
            {
                return Error(HttpStatusCode.NotFound, "shopping_cart_item", "not found");
            }

            var shoppingCartItemsDtos = await shoppingCartItems
                                        .SelectAwait(async shoppingCartItem => await _dtoHelper.PrepareShoppingCartItemDTOAsync(shoppingCartItem))
                                        .ToListAsync();

            var shoppingCartsRootObject = new ShoppingCartItemsRootObject
            {
                ShoppingCartItems = shoppingCartItemsDtos
            };

            var json = JsonFieldsSerializer.Serialize(shoppingCartsRootObject, parameters.Fields);

            return new RawJsonActionResult(json);
        }

        [HttpPost]
        [Route("/api/shopping_cart_items", Name = "CreateShoppingCartItem")]
        [ProducesResponseType(typeof(ShoppingCartItemsRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(string), 422)]
        public async Task<IActionResult> CreateShoppingCartItem(
            [ModelBinder(typeof(JsonModelBinder<ShoppingCartItemDto>))]
            Delta<ShoppingCartItemDto> shoppingCartItemDelta)
        {
            // Here we display the errors if the validation has failed at some point.
            if (!ModelState.IsValid)
            {
                return Error();
            }

            var newShoppingCartItem = await _factory.InitializeAsync();
            shoppingCartItemDelta.Merge(newShoppingCartItem);

            // We know that the product id and customer id will be provided because they are required by the validator.
            // TODO: validate
            var product = await _productService.GetProductByIdAsync(newShoppingCartItem.ProductId);

            if (product == null)
            {
                return Error(HttpStatusCode.NotFound, "product", "not found");
            }

            var customer = await CustomerService.GetCustomerByIdAsync(newShoppingCartItem.CustomerId);

            if (customer == null)
            {
                return Error(HttpStatusCode.NotFound, "customer", "not found");
            }

            var shoppingCartType = (ShoppingCartType)Enum.Parse(typeof(ShoppingCartType), shoppingCartItemDelta.Dto.ShoppingCartType);

            if (!product.IsRental)
            {
                newShoppingCartItem.RentalStartDateUtc = null;
                newShoppingCartItem.RentalEndDateUtc = null;
            }

            var attributesXml = await _productAttributeConverter.ConvertToXmlAsync(shoppingCartItemDelta.Dto.Attributes, product.Id);

            var currentStoreId = _storeContext.GetCurrentStore().Id;

            var warnings = await _shoppingCartService.AddToCartAsync(customer, product, shoppingCartType, currentStoreId, attributesXml, 0M,
                                                          newShoppingCartItem.RentalStartDateUtc, newShoppingCartItem.RentalEndDateUtc,
                                                          shoppingCartItemDelta.Dto.Quantity ?? 1);

            if (warnings.Count > 0)
            {
                foreach (var warning in warnings)
                {
                    ModelState.AddModelError("shopping cart item", warning);
                }

                return Error(HttpStatusCode.BadRequest);
            }
            // the newly added shopping cart item should be the last one
            newShoppingCartItem = (await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart)).LastOrDefault();

            // Preparing the result dto of the new product category mapping
            var newShoppingCartItemDto = await _dtoHelper.PrepareShoppingCartItemDTOAsync(newShoppingCartItem);

            var shoppingCartsRootObject = new ShoppingCartItemsRootObject();

            shoppingCartsRootObject.ShoppingCartItems.Add(newShoppingCartItemDto);

            var json = JsonFieldsSerializer.Serialize(shoppingCartsRootObject, string.Empty);

            return new RawJsonActionResult(json);
        }

        [HttpPut]
        [Route("/api/shopping_cart_items/{id}", Name = "UpdateShoppingCartItem")]
        [ProducesResponseType(typeof(ShoppingCartItemsRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(ErrorsRootObject), 422)]
        public async Task<IActionResult> UpdateShoppingCartItem(
            [ModelBinder(typeof(JsonModelBinder<ShoppingCartItemDto>))]
            Delta<ShoppingCartItemDto> shoppingCartItemDelta)
        {
            // Here we display the errors if the validation has failed at some point.
            if (!ModelState.IsValid)
            {
                return Error();
            }

            // We kno that the id will be valid integer because the validation for this happens in the validator which is executed by the model binder.
            var shoppingCartItemForUpdate = await _shoppingCartItemApiService.GetShoppingCartItemAsync(shoppingCartItemDelta.Dto.Id);

            if (shoppingCartItemForUpdate == null)
            {
                return Error(HttpStatusCode.NotFound, "shopping_cart_item", "not found");
            }

            shoppingCartItemDelta.Merge(shoppingCartItemForUpdate);

            if (!(await _productService.GetProductByIdAsync(shoppingCartItemForUpdate.ProductId)).IsRental)
            {
                shoppingCartItemForUpdate.RentalStartDateUtc = null;
                shoppingCartItemForUpdate.RentalEndDateUtc = null;
            }

            if (shoppingCartItemDelta.Dto.Attributes != null)
            {
                shoppingCartItemForUpdate.AttributesXml = await _productAttributeConverter.ConvertToXmlAsync(shoppingCartItemDelta.Dto.Attributes, shoppingCartItemForUpdate.ProductId);
            }

            var customer = await CustomerService.GetCustomerByIdAsync(shoppingCartItemForUpdate.CustomerId);
            // The update time is set in the service.
            var warnings = await _shoppingCartService.UpdateShoppingCartItemAsync(customer, shoppingCartItemForUpdate.Id,
                                                                       shoppingCartItemForUpdate.AttributesXml, shoppingCartItemForUpdate.CustomerEnteredPrice,
                                                                       shoppingCartItemForUpdate.RentalStartDateUtc, shoppingCartItemForUpdate.RentalEndDateUtc,
                                                                       shoppingCartItemForUpdate.Quantity);

            if (warnings.Count > 0)
            {
                foreach (var warning in warnings)
                {
                    ModelState.AddModelError("shopping cart item", warning);
                }

                return Error(HttpStatusCode.BadRequest);
            }
            shoppingCartItemForUpdate = await _shoppingCartItemApiService.GetShoppingCartItemAsync(shoppingCartItemForUpdate.Id);

            // Preparing the result dto of the new product category mapping
            var newShoppingCartItemDto = await _dtoHelper.PrepareShoppingCartItemDTOAsync(shoppingCartItemForUpdate);

            var shoppingCartsRootObject = new ShoppingCartItemsRootObject();

            shoppingCartsRootObject.ShoppingCartItems.Add(newShoppingCartItemDto);

            var json = JsonFieldsSerializer.Serialize(shoppingCartsRootObject, string.Empty);

            return new RawJsonActionResult(json);
        }

        [HttpDelete]
        [Route("/api/shopping_cart_items/{id}", Name = "DeleteShoppingCartItem")]
        [ProducesResponseType(typeof(void), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [GetRequestsErrorInterceptorActionFilter]
        public async Task<IActionResult> DeleteShoppingCartItem(int id)
        {
            if (id <= 0)
            {
                return Error(HttpStatusCode.BadRequest, "id", "invalid id");
            }

            var shoppingCartItemForDelete = await _shoppingCartItemApiService.GetShoppingCartItemAsync(id);

            if (shoppingCartItemForDelete == null)
            {
                return Error(HttpStatusCode.NotFound, "shopping_cart_item", "not found");
            }

            await _shoppingCartService.DeleteShoppingCartItemAsync(shoppingCartItemForDelete);

            //activity log
            await CustomerActivityService.InsertActivityAsync("DeleteShoppingCartItem", await LocalizationService.GetResourceAsync("ActivityLog.DeleteShoppingCartItem"), shoppingCartItemForDelete);

            return new RawJsonActionResult("{}");
        }
    }
}
