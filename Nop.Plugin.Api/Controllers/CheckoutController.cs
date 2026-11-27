using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Api.Attributes;
using Nop.Plugin.Api.DTO.Errors;
using Nop.Plugin.Api.DTOs.Checkout;
using Nop.Services.Attributes;
using Nop.Services.Authentication;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Services.Shipping;
using Nop.Services.Stores;

namespace Nop.Plugin.Api.Controllers;

[ApiController]
[Authorize(Policy = JwtBearerDefaults.AuthenticationScheme, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Route("api/checkout")]
[ProducesResponseType(401)]
[ProducesResponseType(403)]
[ProducesResponseType(404)]
public class CheckoutController(
    IAuthenticationService authenticationService,
    ICustomerService customerService,
    IStoreContext storeContext,
    IPermissionService permissionService,
    IShoppingCartService shoppingCartService,
    IProductService productService,
    ICountryService countryService,
    IShippingService shippingService,
    IAttributeService<CheckoutAttribute, CheckoutAttributeValue> checkoutAttributeService,
    IStoreMappingService storeMappingService,
    IPaymentPluginManager paymentPluginManager,
    IOrderProcessingService orderProcessingService,
    AddressSettings addressSettings,
    CustomerSettings customerSettings,
    OrderSettings orderSettings) : ControllerBase
{
    [HttpGet("shipping-options", Name = "GetCheckoutShippingOptions")]
    [ApiAudience(ApiAudience.Both)]
    [ProducesResponseType(typeof(ShippingOptionsDto), 200)]
    [ProducesResponseType(typeof(ErrorsRootObject), 400)]
    public async Task<IActionResult> GetShippingOptions(
        [FromQuery(Name = "shipping_address_id"), Range(1, int.MaxValue)] int shippingAddressId,
        [FromQuery(Name = "customer_id"), Range(1, int.MaxValue)] int? customerId = null)
    {
        if (shippingAddressId <= 0)
            return CheckoutError("shipping_address_id", "Supply a shipping address ID.");

        var (customer, storeId, cart, error) = await GetCartAsync(customerId);
        if (error != null)
            return error;

        var address = await customerService.GetCustomerAddressAsync(customer.Id, shippingAddressId);
        if (address == null)
            return NotFound();

        if (!await shoppingCartService.ShoppingCartRequiresShippingAsync(cart))
            return Ok(new ShippingOptionsDto([]));

        if (await countryService.GetCountryByAddressAsync(address) is Country { AllowsShipping: false })
            return CheckoutError("shipping_address_id", "The address country does not allow shipping.");

        var response = await shippingService.GetShippingOptionsAsync(cart, address, customer, storeId: storeId);
        if (!response.Success)
            return CheckoutError("shipping_options", response.Errors.ToArray());

        return Ok(new ShippingOptionsDto(response.ShippingOptions
            .Select(option => new ShippingQuoteDto(option.Name, option.ShippingRateComputationMethodSystemName, option.Rate))
            .ToList()));
    }

    [HttpGet("payment-methods", Name = "GetCheckoutPaymentMethods")]
    [ApiAudience(ApiAudience.Both)]
    [ProducesResponseType(typeof(PaymentMethodsDto), 200)]
    [ProducesResponseType(typeof(ErrorsRootObject), 400)]
    public async Task<IActionResult> GetPaymentMethods(
        [FromQuery(Name = "customer_id"), Range(1, int.MaxValue)] int? customerId = null,
        [FromQuery(Name = "billing_address_id"), Range(1, int.MaxValue)] int? billingAddressId = null)
    {
        var (customer, storeId, cart, error) = await GetCartAsync(customerId);
        if (error != null)
            return error;

        var address = billingAddressId.HasValue
            ? await customerService.GetCustomerAddressAsync(customer.Id, billingAddressId.Value)
            : await customerService.GetCustomerBillingAddressAsync(customer);
        if (billingAddressId.HasValue && address == null)
            return NotFound();

        if (await countryService.GetCountryByAddressAsync(address) is Country { AllowsBilling: false })
            return CheckoutError("billing_address_id", "The address country does not allow billing.");

        if (!await shoppingCartService.ShoppingCartRequiresShippingAsync(cart) &&
            !await orderProcessingService.IsPaymentWorkflowRequiredAsync(cart, false) &&
            (!billingAddressId.HasValue || (await customerService.GetCustomerBillingAddressAsync(customer))?.Id == address.Id) &&
            !await CartCanHaveAdditionalCheckoutChargesAsync(storeId))
            return Ok(new PaymentMethodsDto([]));

        var countryId = addressSettings.CountryEnabled ? address?.CountryId ?? 0 : 0;

        var recurring = await shoppingCartService.ShoppingCartIsRecurringAsync(cart);
        var methods = new List<CheckoutPaymentMethodDto>();
        foreach (var method in await paymentPluginManager.LoadActivePluginsAsync(customer, storeId, countryId))
        {
            if (method.PaymentMethodType is not (PaymentMethodType.Standard or PaymentMethodType.Redirection) ||
                (recurring && method.RecurringPaymentType == RecurringPaymentType.NotSupported) ||
                await method.HidePaymentMethodAsync(cart))
                continue;

            methods.Add(new CheckoutPaymentMethodDto(method.PluginDescriptor.FriendlyName, method.PluginDescriptor.SystemName));
        }

        return Ok(new PaymentMethodsDto(methods));
    }

    private async Task<bool> CartCanHaveAdditionalCheckoutChargesAsync(int storeId)
    {
        foreach (var attribute in await checkoutAttributeService.GetAllAttributesAsync())
        {
            if (attribute.ShippableProductRequired || !attribute.ShouldHaveValues ||
                !await storeMappingService.AuthorizeAsync(attribute, storeId))
                continue;

            var choices = await checkoutAttributeService.GetAttributeValuesAsync(attribute.Id);
            if (choices.Any(choice => choice.PriceAdjustment != 0))
                return true;
        }

        return false;
    }

    private async Task<(Customer Customer, int StoreId, IList<ShoppingCartItem> Cart, IActionResult Error)> GetCartAsync(int? customerId)
    {
        var actor = await authenticationService.GetAuthenticatedCustomerAsync();
        if (actor == null)
            return (null, 0, null, Unauthorized());

        var customer = actor;
        if (customerId.HasValue && customerId.Value != actor.Id)
        {
            if (!await customerService.IsAdminAsync(actor) ||
                !await permissionService.AuthorizeAsync(StandardPermission.Orders.ORDERS_CREATE_EDIT_DELETE, actor))
                return (null, 0, null, StatusCode(403));

            customer = await customerService.GetCustomerByIdAsync(customerId.Value);
            if (customer is not { Active: true, Deleted: false })
                return (null, 0, null, NotFound());
        }
        else if (!await permissionService.AuthorizeAsync(StandardPermission.PublicStore.ENABLE_SHOPPING_CART, actor))
            return (null, 0, null, StatusCode(403));

        if (orderSettings.CheckoutDisabled)
            return (null, 0, null, CheckoutError("checkout", "Checkout is disabled in this store."));

        var storeId = (await storeContext.GetCurrentStoreAsync()).Id;
        var cart = await shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, storeId);
        if (cart.Count == 0)
            return (null, 0, null, CheckoutError("cart", "The shopping cart is empty."));

        if (await customerService.IsGuestAsync(customer) &&
            (!orderSettings.AnonymousCheckoutAllowed ||
             (customerSettings.RequireRegistrationForDownloadableProducts &&
              await productService.HasAnyDownloadableProductAsync(cart.Select(item => item.ProductId).ToArray()))))
            return (null, 0, null, StatusCode(403));

        var warnings = await shoppingCartService.GetShoppingCartWarningsAsync(cart, null, validateCheckoutAttributes: false);
        if (warnings.Count > 0)
            return (null, 0, null, CheckoutError("cart", warnings.ToArray()));

        foreach (var item in cart)
        {
            var product = await productService.GetProductByIdAsync(item.ProductId);
            if (product == null)
                return (null, 0, null, CheckoutError("cart", "A shopping cart product is unavailable."));

            var itemWarnings = await shoppingCartService.GetShoppingCartItemWarningsAsync(customer, item.ShoppingCartType,
                product, storeId, item.AttributesXml, item.CustomerEnteredPrice, item.RentalStartDateUtc,
                item.RentalEndDateUtc, item.Quantity, addRequiredProducts: false, shoppingCartItemId: item.Id);
            if (itemWarnings.Count > 0)
                return (null, 0, null, CheckoutError("cart", itemWarnings.ToArray()));
        }

        return (customer, storeId, cart, null);
    }

    private BadRequestObjectResult CheckoutError(string key, params string[] messages) =>
        BadRequest(new ErrorsRootObject { Errors = new Dictionary<string, List<string>> { [key] = messages.ToList() } });
}
