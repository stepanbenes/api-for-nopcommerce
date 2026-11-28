using LinqToDB;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Plugin.Api.Attributes;
using Nop.Plugin.Api.Authorization.Attributes;
using Nop.Plugin.Api.Delta;
using Nop.Plugin.Api.DTO.Errors;
using Nop.Plugin.Api.DTO.OrderItems;
using Nop.Plugin.Api.DTO.Orders;
using Nop.Plugin.Api.Factories;
using Nop.Plugin.Api.Helpers;
using Nop.Plugin.Api.Infrastructure;
using Nop.Plugin.Api.JSON.ActionResults;
using Nop.Plugin.Api.JSON.Serializers;
using Nop.Plugin.Api.ModelBinders;
using Nop.Plugin.Api.Models.OrdersParameters;
using Nop.Plugin.Api.Services;
using Nop.Services.Authentication;
using Nop.Services.Attributes;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Discounts;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Media;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Services.Shipping;
using Nop.Services.Stores;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Nop.Plugin.Api.Controllers
{
    public class OrdersController : BaseApiController
    {
        private readonly IAddressService _addressService;
        private readonly IDownloadService _downloadService;
        private readonly INopDataProvider _dataProvider;
        private readonly IAttributeService<CheckoutAttribute, CheckoutAttributeValue> _checkoutAttributeService;
        private readonly IAttributeParser<CheckoutAttribute, CheckoutAttributeValue> _checkoutAttributeParser;
        private readonly IDTOHelper _dtoHelper;
        private readonly ILogger<OrdersController> _logger;
        private readonly IFactory<Order> _factory;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IOrderApiService _orderApiService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IOrderService _orderService;
        private readonly IProductAttributeConverter _productAttributeConverter;
        private readonly IPaymentService _paymentService;
        private readonly IPdfService _pdfService;
        private readonly IPermissionService _permissionService;
        private readonly IAuthenticationService _authenticationService;
        private readonly IProductService _productService;
        private readonly IShippingService _shippingService;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly IStoreContext _storeContext;

        // We resolve the order settings this way because of the tests.
        // The auto mocking does not support concreate types as dependencies. It supports only interfaces.
        private OrderSettings _orderSettings;

        public OrdersController(
          IOrderApiService orderApiService,
          IJsonFieldsSerializer jsonFieldsSerializer,
          IAclService aclService,
          ICustomerService customerService,
          IStoreMappingService storeMappingService,
          IStoreService storeService,
          IDiscountService discountService,
          ICustomerActivityService customerActivityService,
          ILocalizationService localizationService,
          IProductService productService,
          IFactory<Order> factory,
          IOrderProcessingService orderProcessingService,
          IOrderService orderService,
          IShoppingCartService shoppingCartService,
          IGenericAttributeService genericAttributeService,
          IStoreContext storeContext,
          IShippingService shippingService,
          IPictureService pictureService,
          IDTOHelper dtoHelper,
          IProductAttributeConverter productAttributeConverter,
          IPaymentService paymentService,
          IPdfService pdfService,
          IPermissionService permissionService,
          IAuthenticationService authenticationService,
          IAddressService addressService,
          IDownloadService downloadService,
          IAttributeService<CheckoutAttribute, CheckoutAttributeValue> checkoutAttributeService,
          IAttributeParser<CheckoutAttribute, CheckoutAttributeValue> checkoutAttributeParser,
          INopDataProvider dataProvider,
          ILogger<OrdersController> logger)
          : base(jsonFieldsSerializer, aclService, customerService, storeMappingService,
               storeService, discountService, customerActivityService, localizationService, pictureService)
        {
            _addressService = addressService;
            _downloadService = downloadService;
            _dataProvider = dataProvider;
            _logger = logger;
            _checkoutAttributeService = checkoutAttributeService;
            _checkoutAttributeParser = checkoutAttributeParser;
            _orderApiService = orderApiService;
            _factory = factory;
            _orderProcessingService = orderProcessingService;
            _orderService = orderService;
            _shoppingCartService = shoppingCartService;
            _genericAttributeService = genericAttributeService;
            _storeContext = storeContext;
            _shippingService = shippingService;
            _dtoHelper = dtoHelper;
            _productService = productService;
            _productAttributeConverter = productAttributeConverter;
            _paymentService = paymentService;
            _pdfService = pdfService;
            _permissionService = permissionService;
            _authenticationService = authenticationService;
        }

        private OrderSettings OrderSettings => _orderSettings ?? (_orderSettings = EngineContext.Current.Resolve<OrderSettings>());

        /// <summary>
        ///     Receive a list of all Orders
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="400">Bad Request</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/orders", Name = "GetOrders")]
        [ApiAudience(ApiAudience.BackOffice)]
        [ProducesResponseType(typeof(OrdersRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [GetRequestsErrorInterceptorActionFilter]
        [UnpagedCollection]
        public async Task<IActionResult> GetOrders([FromQuery] OrdersParametersModel parameters)
        {
            if (parameters.Page < Constants.Configurations.DefaultPageValue)
            {
                return Error(HttpStatusCode.BadRequest, "page", "Invalid page parameter");
            }

            if (parameters.Limit < Constants.Configurations.MinLimit || parameters.Limit > Constants.Configurations.MaxLimit)
            {
                return Error(HttpStatusCode.BadRequest, "page", "Invalid limit parameter");
            }

            if (!await CheckPermissions(parameters.CustomerId))
            {
                return AccessDenied();
            }

            var storeId = (await _storeContext.GetCurrentStoreAsync()).Id;

            var orders = await _orderApiService.GetOrdersAsync(parameters.Ids, parameters.CreatedAtMin,
                                parameters.CreatedAtMax,
                                GetCollectionLimit(parameters.Limit), parameters.Page, parameters.SinceId,
                                parameters.Status.HasValue ? (Core.Domain.Orders.OrderStatus)parameters.Status : null,
                                parameters.PaymentStatus.HasValue ? (Core.Domain.Payments.PaymentStatus)parameters.PaymentStatus : null,
                                parameters.ShippingStatus.HasValue ? (Core.Domain.Shipping.ShippingStatus)parameters.ShippingStatus : null,
                                parameters.CustomerId, storeId);

            IList<OrderDto> ordersAsDtos = await orders.SelectAwait(async x => await _dtoHelper.PrepareOrderDTOAsync(x)).ToListAsync();

            var ordersRootObject = new OrdersRootObject
            {
                Orders = ordersAsDtos
            };

            var json = JsonFieldsSerializer.Serialize(ordersRootObject, parameters.Fields);

            return new RawJsonActionResult(json);
        }

        /// <summary>
        ///     Receive a count of all Orders
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/orders/count", Name = "GetOrdersCount")]
        [ApiAudience(ApiAudience.Both)]
        [ProducesResponseType(typeof(OrdersCountRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [GetRequestsErrorInterceptorActionFilter]
        public async Task<IActionResult> GetOrdersCount([FromQuery] OrdersCountParametersModel parameters)
        {
            if (!await CheckPermissions(parameters.CustomerId))
            {
                return AccessDenied();
            }

            // TODO: make async

            var storeId = (await _storeContext.GetCurrentStoreAsync()).Id;

            var ordersCount = await _orderApiService.GetOrdersCountAsync(parameters.CreatedAtMin, parameters.CreatedAtMax,
                                      parameters.Status.HasValue ? (Core.Domain.Orders.OrderStatus)parameters.Status : null,
                                      parameters.PaymentStatus.HasValue ? (Core.Domain.Payments.PaymentStatus)parameters.PaymentStatus : null,
                                      parameters.ShippingStatus.HasValue ? (Core.Domain.Shipping.ShippingStatus)parameters.ShippingStatus : null,
                                      parameters.CustomerId, storeId);

            var ordersCountRootObject = new OrdersCountRootObject
            {
                Count = ordersCount
            };

            return Ok(ordersCountRootObject);
        }

        /// <summary>
        ///     Retrieve order by spcified id
        /// </summary>
        /// ///
        /// <param name="id">Id of the order</param>
        /// <param name="fields">Fields from the order you want your json to contain</param>
        /// <response code="200">OK</response>
        /// <response code="404">Not Found</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/orders/{id}", Name = "GetOrderById")]
        [ApiAudience(ApiAudience.BackOffice)]
        [ProducesResponseType(typeof(OrdersRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [GetRequestsErrorInterceptorActionFilter]
        public async Task<IActionResult> GetOrderById([FromRoute] int id, [FromQuery] string fields = "")
        {
            if (id <= 0)
            {
                return Error(HttpStatusCode.BadRequest, "id", "invalid id");
            }

            var order = await GetOrderForCurrentStoreAsync(id);

            if (order == null)
            {
                return Error(HttpStatusCode.NotFound, "order", "not found");
            }

            if (!await CheckPermissions(order.CustomerId))
            {
                return AccessDenied();
            }

            var ordersRootObject = new OrdersRootObject();

            var orderDto = await _dtoHelper.PrepareOrderDTOAsync(order);
            ordersRootObject.Orders.Add(orderDto);

            var json = JsonFieldsSerializer.Serialize(ordersRootObject, fields);

            return new RawJsonActionResult(json);
        }

        /// <summary>
        ///     Retrieve all orders for customer
        /// </summary>
        /// <param name="customerId">Id of the customer whoes orders you want to get</param>
        /// <response code="200">OK</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/orders/customer/{customerId}", Name = "GetOrdersByCustomerId")]
        [ApiAudience(ApiAudience.BackOffice)]
        [ProducesResponseType(typeof(OrdersRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [GetRequestsErrorInterceptorActionFilter]
        public async Task<IActionResult> GetOrdersByCustomerId([FromRoute] int customerId)
        {
            if (!await CheckPermissions(customerId))
            {
                return AccessDenied();
            }

            var storeId = (await _storeContext.GetCurrentStoreAsync()).Id;
            IList<OrderDto> ordersForCustomer = await (await _orderApiService.GetOrdersByCustomerIdAsync(customerId, storeId)).SelectAwait(async x => await _dtoHelper.PrepareOrderDTOAsync(x)).ToListAsync();

            var ordersRootObject = new OrdersRootObject
            {
                Orders = ordersForCustomer
            };

            return Ok(ordersRootObject);
        }

        [HttpGet]
        [Route("/api/checkout_attributes", Name = "GetCheckoutAttributes")]
        [ApiAudience(ApiAudience.Storefront)]
        [ProducesResponseType(typeof(CheckoutAttributesRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(void), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(void), (int)HttpStatusCode.Forbidden)]
        public async Task<IActionResult> GetCheckoutAttributes(
            [FromQuery(Name = "shipping_required")] bool? shippingRequired = null,
            [FromQuery(Name = "include_conditional")] bool includeConditional = false)
        {
            var customer = await _authenticationService.GetAuthenticatedCustomerAsync();
            if (customer == null ||
                !await _permissionService.AuthorizeAsync(StandardPermission.PublicStore.ENABLE_SHOPPING_CART, customer))
                return AccessDenied();

            var storeId = (await _storeContext.GetCurrentStoreAsync()).Id;
            if (!shippingRequired.HasValue)
            {
                var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, storeId);
                shippingRequired = await _shoppingCartService.ShoppingCartRequiresShippingAsync(cart);
            }

            var selectedAttributes = await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.CheckoutAttributes, storeId);
            var result = new CheckoutAttributesRootObject();
            foreach (var attribute in await _checkoutAttributeService.GetAllAttributesAsync())
            {
                if ((attribute.ShippableProductRequired && !shippingRequired.Value) ||
                    !await StoreMappingService.AuthorizeAsync(attribute, storeId) ||
                    (!includeConditional && await _checkoutAttributeParser.IsConditionMetAsync(attribute.ConditionAttributeXml, selectedAttributes) == false))
                    continue;

                var dto = new CheckoutAttributeDto
                {
                    Id = attribute.Id,
                    Name = await LocalizationService.GetLocalizedAsync(attribute, item => item.Name),
                    Required = attribute.IsRequired,
                    ControlType = attribute.AttributeControlType.ToString(),
                    Condition = CheckoutAttributeSelections.DescribeCondition(attribute, _checkoutAttributeParser)
                };

                if (attribute.ShouldHaveValues)
                {
                    foreach (var choice in await _checkoutAttributeService.GetAttributeValuesAsync(attribute.Id))
                    {
                        dto.Choices.Add(new CheckoutAttributeChoiceDto
                        {
                            Id = choice.Id,
                            Name = await LocalizationService.GetLocalizedAsync(choice, item => item.Name),
                            Preselected = choice.IsPreSelected
                        });
                    }
                }

                result.CheckoutAttributes.Add(dto);
            }

            return Ok(result);
        }

        [HttpPost]
        [Route("/api/orders", Name = "CreateOrder")]
        [ApiAudience(ApiAudience.BackOffice)]
        [ProducesResponseType(typeof(OrdersRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.Conflict)]
        [ProducesResponseType(typeof(ErrorsRootObject), 422)]
        public async Task<IActionResult> CreateOrder(
          [FromBody]
      [ModelBinder(typeof(JsonModelBinder<OrderDto>))]
      Delta<OrderDto> orderDelta,
          [FromHeader(Name = "Idempotency-Key")] string idempotencyKey = null)
        {
            if (!ModelState.IsValid)
                return Error();

            if (orderDelta.Dto.CustomerId == null)
            {
                return Error(HttpStatusCode.BadRequest, "customerId", "invalid customer id");
            }

            if (orderDelta.Dto.BillingAddress == null)
            {
                return Error(HttpStatusCode.BadRequest, "billingAddress", "missing billing address");
            }
            if (orderDelta.Dto.BillingAddress.Id == 0)
            {
                return Error(HttpStatusCode.BadRequest, "billingAddress", "non-existing billing address");
            }
            if (orderDelta.Dto.ShippingAddress != null && orderDelta.Dto.ShippingAddress.Id == 0) // shipping address CAN be null, but if it is not, it must exist in db
            {
                return Error(HttpStatusCode.BadRequest, "shippingAddress", "non-existing shipping address");
            }

            if (!await CheckPermissions(orderDelta.Dto.CustomerId, isCheckout: true))
            {
                // TODO: check _orderSettings.AnonymousCheckoutAllowed if IsGuest
                return AccessDenied();
            }

            // We doesn't have to check for value because this is done by the order validator.
            var customer = await CustomerService.GetCustomerByIdAsync(orderDelta.Dto.CustomerId.Value);

            if (customer == null)
            {
                return Error(HttpStatusCode.NotFound, "customer", "not found");
            }

            var storeId = (await _storeContext.GetCurrentStoreAsync()).Id;
            if (orderDelta.Dto.StoreId.HasValue && orderDelta.Dto.StoreId != storeId)
                return Error(HttpStatusCode.BadRequest, "store_id", "Order store must match the current store.");

            Guid? orderGuid = null;
            if (idempotencyKey != null || Request.Headers.ContainsKey("Idempotency-Key"))
            {
                if (Request.Headers["Idempotency-Key"].Count > 1 || !Guid.TryParse(idempotencyKey, out var key) || key == Guid.Empty)
                    return Error(HttpStatusCode.BadRequest, "Idempotency-Key", "Supply a non-empty UUID.");

                orderGuid = GetRequestOrderGuid(key, customer.Id, storeId);
                var replay = await ReplayOrderAsync(orderGuid.Value, customer.Id, storeId);
                if (replay != null)
                    return replay;
            }

            Order placedOrder;
            IActionResult failure;
            try
            {
                (placedOrder, failure) = await PlaceNewOrderAsync(orderDelta, customer, storeId, orderGuid);
            }
            catch (Exception) when (orderGuid.HasValue)
            {
                // A competing transaction may have committed while this transaction was rolled back.
                var replay = await ReplayOrderAsync(orderGuid.Value, customer.Id, storeId);
                if (replay != null)
                    return replay;

                throw;
            }

            if (failure != null)
                return orderGuid.HasValue
                    ? await ReplayOrderAsync(orderGuid.Value, customer.Id, storeId) ?? failure
                    : failure;

            var followUp = false;
            try
            {
                await _paymentService.PostProcessPaymentAsync(new PostProcessPaymentRequest { Order = placedOrder });
            }
            catch (Exception exception)
            {
                followUp = true;
                _logger.LogWarning("Payment follow-up for placed order {OrderId} failed with {ExceptionType}.",
                    placedOrder.Id, exception.GetType().Name);
            }

            try
            {
                await CustomerActivityService.InsertActivityAsync("AddNewOrder",
                    await LocalizationService.GetResourceAsync("ActivityLog.AddNewOrder"), placedOrder);
            }
            catch (Exception exception)
            {
                followUp = true;
                _logger.LogWarning("Activity logging for placed order {OrderId} failed with {ExceptionType}.",
                    placedOrder.Id, exception.GetType().Name);
            }

            return await PlacedOrderResponseAsync(placedOrder, followUp);
        }

        private async Task<(Order Order, IActionResult Error)> PlaceNewOrderAsync(
            Delta<OrderDto> orderDelta, Customer customer, int storeId, Guid? orderGuid)
        {
            using var transaction = _dataProvider.CreateTransactionScope();
            if (orderGuid.HasValue)
            {
                // Serialize checkout before the payment provider is called, across app instances.
                var activeCustomer = _dataProvider.GetTable<Customer>().Where(row => row.Id == customer.Id && !row.Deleted);
                await activeCustomer.Set(row => row.LastActivityDateUtc, row => row.LastActivityDateUtc).UpdateAsync();
                customer = await activeCustomer.SingleOrDefaultAsync();
                if (customer is null)
                    return (null, Error(HttpStatusCode.NotFound, "customer", "not found"));

                var existing = await _orderService.GetOrderByGuidAsync(orderGuid.Value);
                if (existing != null)
                    return (null, Error(HttpStatusCode.Conflict, "Idempotency-Key", "This key already belongs to an order."));
            }

            var billingAddress = await CustomerService.GetCustomerAddressAsync(customer.Id, orderDelta.Dto.BillingAddress.Id);
            if (billingAddress == null)
                return (null, Error(HttpStatusCode.BadRequest, "billingAddress", "non-existing billing address"));
            var shippingAddressId = orderDelta.Dto.ShippingAddress?.Id;
            var shippingAddress = shippingAddressId.HasValue
                ? await CustomerService.GetCustomerAddressAsync(customer.Id, shippingAddressId.Value)
                : null;

            if (shippingAddressId.HasValue && shippingAddress == null)
                return (null, Error(HttpStatusCode.BadRequest, "shippingAddress", "non-existing shipping address"));

            if (orderDelta.Dto.OrderItems != null && await AddOrderItemsToCartAsync(orderDelta.Dto.OrderItems, customer, storeId))
                return (null, Error(HttpStatusCode.BadRequest));

            var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, storeId);
            if (cart.Count == 0)
                return (null, Error(HttpStatusCode.BadRequest, "order", "Cart is empty."));

            var shippingRequired = await _shoppingCartService.ShoppingCartRequiresShippingAsync(cart);
            var selections = orderDelta.Dto.CheckoutAttributes;
            var existingCheckoutAttributes = selections == null
                ? await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.CheckoutAttributes, storeId)
                : null;
            var checkoutAttributesXml = await CheckoutAttributeSelections.BuildAsync(
                selections ?? [], storeId, shippingRequired, _checkoutAttributeService, _checkoutAttributeParser,
                StoreMappingService, _downloadService, ModelState, existingCheckoutAttributes);
            if (!ModelState.IsValid)
                return (null, Error(HttpStatusCode.BadRequest));

            var warnings = await _shoppingCartService.GetShoppingCartWarningsAsync(cart, checkoutAttributesXml, validateCheckoutAttributes: true);
            if (warnings.Count > 0)
            {
                foreach (var warning in warnings)
                    ModelState.AddModelError("order", warning);
                return (null, Error(HttpStatusCode.BadRequest));
            }

            if (shippingRequired)
            {
                if (shippingAddress == null)
                    return (null, Error(HttpStatusCode.BadRequest, "shippingAddress", "missing shipping address"));

                var shippingOption = await GetShippingOptionAsync(orderDelta.Dto.ShippingRateComputationMethodSystemName,
                    orderDelta.Dto.ShippingMethod, storeId, customer, shippingAddress, cart);
                if (shippingOption == null)
                    return (null, Error(HttpStatusCode.BadRequest));

                await _genericAttributeService.SaveAttributeAsync(customer,
                    NopCustomerDefaults.SelectedShippingOptionAttribute, shippingOption, storeId);
            }

            var newOrder = await _factory.InitializeAsync();
            orderDelta.Merge(newOrder);
            newOrder.CustomerId = customer.Id;
            newOrder.StoreId = storeId;
            newOrder.OrderGuid = orderGuid ?? Guid.NewGuid();

            customer.BillingAddressId = newOrder.BillingAddressId = orderDelta.Dto.BillingAddress.Id;
            customer.ShippingAddressId = newOrder.ShippingAddressId = shippingAddressId;
            await CustomerService.UpdateCustomerAsync(customer);

            if (selections != null || checkoutAttributesXml != existingCheckoutAttributes)
                await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.CheckoutAttributes, checkoutAttributesXml, storeId);

            PlaceOrderResult placeOrderResult;
            try
            {
                placeOrderResult = await PlaceOrderAsync(newOrder, customer);
            }
            catch (NopException exception)
            {
                return (null, Error(HttpStatusCode.BadRequest, "order placement", exception.Message));
            }

            if (!placeOrderResult.Success)
            {
                foreach (var error in placeOrderResult.Errors)
                {
                    ModelState.AddModelError("order placement", error);
                }

                return (null, Error(HttpStatusCode.BadRequest));
            }

            transaction.Complete();
            return (placeOrderResult.PlacedOrder, null);
        }

        private static Guid GetRequestOrderGuid(Guid key, int customerId, int storeId)
        {
            var scope = FormattableString.Invariant($"Nop.Plugin.Api/order/{storeId}/{customerId}/{key:N}");
            return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(scope)).AsSpan(0, 16));
        }

        private async Task<IActionResult> ReplayOrderAsync(Guid orderGuid, int customerId, int storeId)
        {
            var order = await _orderService.GetOrderByGuidAsync(orderGuid);
            if (order is null)
                return null;

            if (order.Deleted || order.CustomerId != customerId || order.StoreId != storeId)
                return Error(HttpStatusCode.Conflict, "Idempotency-Key", "This key already belongs to an order that is unavailable.");

            Response.Headers["Idempotency-Replayed"] = "true";
            return await PlacedOrderResponseAsync(order);
        }

        private async Task<IActionResult> PlacedOrderResponseAsync(Order order, bool followUp = false)
        {
            try
            {
                var detail = await _dtoHelper.PrepareOrderDTOAsync(order);
                var result = new OrdersRootObject { Orders = [detail] };
                var json = JsonFieldsSerializer.Serialize(result, string.Empty);
                if (followUp)
                    SetOrderFollowUpHeader(order.Id);

                return new RawJsonActionResult(json);
            }
            catch (Exception exception)
            {
                _logger.LogWarning("Response details for placed order {OrderId} failed with {ExceptionType}.",
                    order.Id, exception.GetType().Name);
                SetOrderFollowUpHeader(order.Id);
                return new RawJsonActionResult(System.Text.Json.JsonSerializer.Serialize(new
                {
                    orders = new[] { new { id = order.Id, customer_id = order.CustomerId, store_id = order.StoreId } }
                }));
            }
        }

        private void SetOrderFollowUpHeader(int orderId)
        {
            Response.Headers["X-Order-Follow-Up"] = $"Order was placed. Check GET /api/orders/{orderId} and confirm payment status before acting.";
        }

        [HttpPut]
        [Route("/api/orders/{id}", Name = "UpdateOrder")]
        [ApiAudience(ApiAudience.BackOffice)]
        [AuthorizePermission(StandardPermission.Orders.ORDERS_CREATE_EDIT_DELETE)]
        [ProducesResponseType(typeof(OrdersRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(ErrorsRootObject), 422)]
        public async Task<IActionResult> UpdateOrder(
          [FromBody]
      [ModelBinder(typeof(JsonModelBinder<OrderDto>))]
      Delta<OrderDto> orderDelta)
        {
            // Here we display the errors if the validation has failed at some point.
            if (!ModelState.IsValid)
            {
                return Error();
            }

            var currentOrder = await GetOrderForCurrentStoreAsync(orderDelta.Dto.Id);

            if (currentOrder == null)
            {
                return Error(HttpStatusCode.NotFound, "order", "not found");
            }

            var customer = await CustomerService.GetCustomerByIdAsync(currentOrder.CustomerId);

            if ((orderDelta.Dto.StoreId.HasValue && orderDelta.Dto.StoreId != currentOrder.StoreId) ||
                (orderDelta.Dto.CustomerId.HasValue && orderDelta.Dto.CustomerId != currentOrder.CustomerId))
                return Error(HttpStatusCode.BadRequest, "order", "The order store and customer cannot be changed.");

            var submittedBillingAddress = orderDelta.Dto.BillingAddress;
            var billingAddress = submittedBillingAddress != null
                ? await CustomerService.GetCustomerAddressAsync(currentOrder.CustomerId, submittedBillingAddress.Id)
                : null;
            if (submittedBillingAddress != null && billingAddress == null)
                return Error(HttpStatusCode.BadRequest, "billingAddress", "non-existing billing address");

            var submittedShippingAddress = orderDelta.Dto.ShippingAddress;
            var shippingAddress = submittedShippingAddress != null
                ? await CustomerService.GetCustomerAddressAsync(customer.Id, submittedShippingAddress.Id)
                : null;
            if (submittedShippingAddress != null && shippingAddress == null)
                return Error(HttpStatusCode.BadRequest, "shippingAddress", "non-existing shipping address");

            if (orderDelta.HasProperty(nameof(OrderDto.ShippingRateComputationMethodSystemName)) ||
                orderDelta.HasProperty(nameof(OrderDto.ShippingMethod)))
            {
                var orderItems = await _orderService.GetOrderItemsAsync(currentOrder.Id);
                var storeId = orderDelta.Dto.StoreId ?? currentOrder.StoreId;
                var cart = BuildShoppingCartItemsFromOrderItems(orderItems, customer.Id, storeId);
                if (await _shoppingCartService.ShoppingCartRequiresShippingAsync(cart))
                {
                    if (shippingAddress == null && currentOrder.ShippingAddressId.HasValue)
                        shippingAddress = await _addressService.GetAddressByIdAsync(currentOrder.ShippingAddressId.Value);

                    if (shippingAddress == null)
                        return Error(HttpStatusCode.BadRequest, "shippingAddress", "missing shipping address");

                    if (await GetShippingOptionAsync(
                        orderDelta.Dto.ShippingRateComputationMethodSystemName ?? currentOrder.ShippingRateComputationMethodSystemName,
                        orderDelta.Dto.ShippingMethod ?? currentOrder.ShippingMethod,
                        storeId, customer, shippingAddress, cart) == null)
                        return Error(HttpStatusCode.BadRequest);
                }
            }

            using (var transaction = _dataProvider.CreateTransactionScope())
            {
                orderDelta.Merge(currentOrder);

                if (billingAddress != null)
                {
                    var snapshot = _addressService.CloneAddress(billingAddress);
                    await _addressService.InsertAddressAsync(snapshot);
                    currentOrder.BillingAddressId = snapshot.Id;
                }

                if (submittedShippingAddress != null)
                {
                    var snapshot = _addressService.CloneAddress(shippingAddress);
                    await _addressService.InsertAddressAsync(snapshot);
                    currentOrder.ShippingAddressId = snapshot.Id;
                }

                await _orderService.UpdateOrderAsync(currentOrder);
                transaction.Complete();
            }

            await CustomerActivityService.InsertActivityAsync("UpdateOrder", await LocalizationService.GetResourceAsync("ActivityLog.UpdateOrder"), currentOrder);

            var ordersRootObject = new OrdersRootObject();

            var placedOrderDto = await _dtoHelper.PrepareOrderDTOAsync(currentOrder);

            ordersRootObject.Orders.Add(placedOrderDto);

            var json = JsonFieldsSerializer.Serialize(ordersRootObject, string.Empty);

            return new RawJsonActionResult(json);
        }

        [HttpDelete]
        [Route("/api/orders/{id}", Name = "DeleteOrder")]
        [ApiAudience(ApiAudience.BackOffice)]
        [AuthorizePermission(StandardPermission.Orders.ORDERS_CREATE_EDIT_DELETE)]
        [ProducesResponseType(typeof(void), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(ErrorsRootObject), 422)]
        [GetRequestsErrorInterceptorActionFilter]
        public async Task<IActionResult> DeleteOrder([FromRoute] int id)
        {
            if (id <= 0)
            {
                return Error(HttpStatusCode.BadRequest, "id", "invalid id");
            }

            var orderToDelete = await GetOrderForCurrentStoreAsync(id);

            if (orderToDelete == null)
            {
                return Error(HttpStatusCode.NotFound, "order", "not found");
            }

            await _orderProcessingService.DeleteOrderAsync(orderToDelete);

            //activity log
            await CustomerActivityService.InsertActivityAsync("DeleteOrder", await LocalizationService.GetResourceAsync("ActivityLog.DeleteOrder"), orderToDelete);

            return new RawJsonActionResult("{}");
        }

        [HttpGet]
        [Route("/api/orders/{orderId}/pdf-invoice", Name = "GetPdfInvoice")]
        [ApiAudience(ApiAudience.Both)]
        [ProducesResponseType(typeof(DTOs.BinaryFileDto), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.Unauthorized)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.NotFound)]
        public async Task<IActionResult> GetPdfInvoice([FromRoute] int orderId)
        {
            var order = await GetOrderForCurrentStoreAsync(orderId);
            if (order == null || order.Deleted)
            {
                return NotFound();
            }

            if (!await CheckPermissions(order.CustomerId))
            {
                return AccessDenied();
            }

            var orders = new List<Order> { order };
            byte[] bytes;
            await using (var stream = new MemoryStream())
            {
                await _pdfService.PrintOrdersToPdfAsync(stream, orders);
                bytes = stream.ToArray();
            }

            var document = new DTOs.BinaryFileDto
            {
                FileName = $"order_{order.OrderGuid:N}.pdf",
                Content = bytes,
                MimeType = "application/pdf",
                CreatedAt = DateTimeOffset.Now,
            };

            return Ok(document);
        }

        #region Private methods

        private async Task<Order> GetOrderForCurrentStoreAsync(int orderId)
        {
            var store = await _storeContext.GetCurrentStoreAsync();
            return await _orderApiService.GetOrderByIdAsync(orderId, store.Id);
        }

        private async Task<bool> CheckPermissions(int? customerId, bool isCheckout = false)
        {
            var currentCustomer = await _authenticationService.GetAuthenticatedCustomerAsync();
            if (currentCustomer is null) // authenticated, but does not exist in db
                return false;
            if (customerId.HasValue && currentCustomer.Id == customerId)
            {
                // if I want to handle my own orders, check only public store permission
                return await _permissionService.AuthorizeAsync(StandardPermission.PublicStore.ENABLE_SHOPPING_CART, currentCustomer);
            }
            return await _permissionService.AuthorizeAsync(isCheckout
                ? StandardPermission.Orders.ORDERS_CREATE_EDIT_DELETE
                : StandardPermission.Orders.ORDERS_VIEW, currentCustomer);
        }

        private async Task<ShippingOption> GetShippingOptionAsync(
            string shippingProvider, string shippingOptionName, int storeId, Customer customer,
            Address shippingAddress, IList<ShoppingCartItem> cart)
        {
            if (string.IsNullOrEmpty(shippingProvider))
            {
                ModelState.AddModelError("shipping_rate_computation_method_system_name",
                    "Please provide shipping_rate_computation_method_system_name");
                return null;
            }

            if (string.IsNullOrEmpty(shippingOptionName))
            {
                ModelState.AddModelError("shipping_option_name", "Please provide shipping_option_name");
                return null;
            }

            var response = await _shippingService.GetShippingOptionsAsync(cart, shippingAddress, customer, shippingProvider, storeId);
            if (!response.Success)
            {
                foreach (var error in response.Errors)
                    ModelState.AddModelError("shipping_option", error);
                return null;
            }

            var option = response.ShippingOptions.FirstOrDefault(option =>
                string.Equals(option.Name, shippingOptionName, StringComparison.OrdinalIgnoreCase));
            if (option == null)
                ModelState.AddModelError("shipping_option", "The requested shipping option is unavailable.");

            return option;
        }

        private List<ShoppingCartItem> BuildShoppingCartItemsFromOrderItems(IList<OrderItem> orderItems, int customerId, int storeId)
        {
            var shoppingCartItems = new List<ShoppingCartItem>();

            foreach (var orderItem in orderItems)
            {
                shoppingCartItems.Add(new ShoppingCartItem
                {
                    ProductId = orderItem.ProductId,
                    CustomerId = customerId,
                    Quantity = orderItem.Quantity,
                    AttributesXml = orderItem.AttributesXml,
                    RentalStartDateUtc = orderItem.RentalStartDateUtc,
                    RentalEndDateUtc = orderItem.RentalEndDateUtc,
                    StoreId = storeId,
                    ShoppingCartType = ShoppingCartType.ShoppingCart
                });
            }

            return shoppingCartItems;
        }

        private async Task<PlaceOrderResult> PlaceOrderAsync(Order newOrder, Customer customer)
        {
            var processPaymentRequest = new ProcessPaymentRequest
            {
                StoreId = newOrder.StoreId,
                CustomerId = customer.Id,
                PaymentMethodSystemName = newOrder.PaymentMethodSystemName,
                // TODO: fill credit card info,
                OrderGuid = newOrder.OrderGuid,
                OrderGuidGeneratedOnUtc = DateTime.UtcNow,
            };

            //_paymentService.GenerateOrderGuid(processPaymentRequest);

            return await _orderProcessingService.PlaceOrderAsync(processPaymentRequest);
        }

        private async Task<bool> AddOrderItemsToCartAsync(ICollection<OrderItemDto> orderItems, Customer customer, int storeId)
        {
            var shouldReturnError = false;

            foreach (var orderItem in orderItems)
            {
                if (orderItem.ProductId != null)
                {
                    var product = await _productService.GetProductByIdAsync(orderItem.ProductId.Value);

                    if (product == null)
                    {
                        ModelState.AddModelError("order", $"Product {orderItem.ProductId} was not found.");
                        shouldReturnError = true;
                        continue;
                    }

                    if (!product.IsRental)
                    {
                        orderItem.RentalStartDateUtc = null;
                        orderItem.RentalEndDateUtc = null;
                    }

                    var attributesXml = await _productAttributeConverter.ConvertToXmlAsync(orderItem.Attributes?.ToList(), product.Id);

                    var errors = await _shoppingCartService.AddToCartAsync(customer, product,
                                          ShoppingCartType.ShoppingCart, storeId, attributesXml,
                                          0M, orderItem.RentalStartDateUtc, orderItem.RentalEndDateUtc,
                                          orderItem.Quantity ?? 1);

                    if (errors.Count > 0)
                    {
                        foreach (var error in errors)
                        {
                            ModelState.AddModelError("order", error);
                        }

                        shouldReturnError = true;
                    }
                }
            }

            return shouldReturnError;
        }

        #endregion
    }
}
