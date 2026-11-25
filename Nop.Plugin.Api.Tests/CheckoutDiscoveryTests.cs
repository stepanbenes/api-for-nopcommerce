using NUnit.Framework;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Stores;
using Nop.Plugin.Api.Controllers;
using Nop.Plugin.Api.DTOs.Checkout;
using Nop.Services.Attributes;
using Nop.Services.Authentication;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Services.Security;
using Nop.Services.Shipping;
using Nop.Services.Stores;

namespace Nop.Plugin.Api.Tests;

[TestFixture]
public class CheckoutDiscoveryTests
{
    private readonly Customer _customer = new() { Id = 10, Active = true };
    private Mock<ICustomerService> _customers;
    private Mock<IShoppingCartService> _carts;
    private Mock<IShippingService> _shipping;
    private Mock<IPaymentPluginManager> _payments;
    private CheckoutController _controller;
    private Mock<ICountryService> _countries;
    private Mock<IOrderProcessingService> _orderProcessing;
    private Mock<IAttributeService<CheckoutAttribute, CheckoutAttributeValue>> _attributes;
    private AddressSettings _addressSettings;
    private List<ShoppingCartItem> _cart;

    [SetUp]
    public void SetUp()
    {
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(x => x.GetAuthenticatedCustomerAsync()).ReturnsAsync(_customer);
        _customers = new Mock<ICustomerService>();
        var store = new Mock<IStoreContext>();
        store.Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Store { Id = 3 });
        var permissions = new Mock<IPermissionService>();
        permissions.Setup(x => x.AuthorizeAsync(StandardPermission.PublicStore.ENABLE_SHOPPING_CART, _customer))
            .ReturnsAsync(true);
        _carts = new Mock<IShoppingCartService>();
        var product = new Product { Id = 4 };
        var products = new Mock<IProductService>();
        products.Setup(x => x.GetProductByIdAsync(product.Id)).ReturnsAsync(product);
        _cart = new List<ShoppingCartItem>
        {
            new() { Id = 12, CustomerId = 10, ProductId = product.Id, StoreId = 3, ShoppingCartType = ShoppingCartType.ShoppingCart, Quantity = 1 }
        };
        _carts.Setup(x => x.GetShoppingCartItemWarningsAsync(_customer, ShoppingCartType.ShoppingCart,
                product, 3, null, 0, null, null, 1, false, 12, true, true, true, true, true))
            .ReturnsAsync(new List<string>());
        _carts.Setup(x => x.GetShoppingCartAsync(_customer, ShoppingCartType.ShoppingCart, 3, null, null, null, null))
            .ReturnsAsync(_cart);
        _carts.Setup(x => x.GetShoppingCartWarningsAsync(_cart, null, false)).ReturnsAsync(new List<string>());
        _carts.Setup(x => x.ShoppingCartRequiresShippingAsync(_cart)).ReturnsAsync(true);
        _carts.Setup(x => x.ShoppingCartIsRecurringAsync(_cart)).ReturnsAsync(true);
        _shipping = new Mock<IShippingService>(MockBehavior.Strict);
        _payments = new Mock<IPaymentPluginManager>(MockBehavior.Strict);
        _countries = new Mock<ICountryService>();
        _countries.Setup(x => x.GetCountryByAddressAsync(It.Is<Address>(address => address != null && address.CountryId == 6)))
            .ReturnsAsync(new Country { Id = 6, AllowsBilling = true, AllowsShipping = true });
        _orderProcessing = new Mock<IOrderProcessingService>();
        _orderProcessing.Setup(x => x.IsPaymentWorkflowRequiredAsync(_cart, false)).ReturnsAsync(true);
        _attributes = new Mock<IAttributeService<CheckoutAttribute, CheckoutAttributeValue>>();
        _attributes.Setup(x => x.GetAllAttributesAsync()).ReturnsAsync(new List<CheckoutAttribute>());
        var storeMapping = new Mock<IStoreMappingService>();
        storeMapping.Setup(x => x.AuthorizeAsync(It.IsAny<CheckoutAttribute>(), 3)).ReturnsAsync(true);
        _addressSettings = new AddressSettings { CountryEnabled = true };
        _controller = new CheckoutController(auth.Object, _customers.Object, store.Object, permissions.Object,
            _carts.Object, products.Object, _countries.Object, _shipping.Object,
            _attributes.Object, storeMapping.Object, _payments.Object, _orderProcessing.Object, _addressSettings,
            new CustomerSettings(), new OrderSettings());
    }

    [Test]
    public async Task CannotDiscoverAnotherCustomersCart()
    {
        var result = await _controller.GetShippingOptions(20, customerId: 11);

        Assert.That(result, Is.TypeOf<StatusCodeResult>().With.Property("StatusCode").EqualTo(403));
        _carts.VerifyNoOtherCalls();
    }

    [Test]
    public async Task CannotQuoteForAnAddressOutsideTheCustomersAddressBook()
    {
        Assert.That(await _controller.GetShippingOptions(20), Is.TypeOf<NotFoundResult>());
        _customers.Verify(x => x.GetCustomerAddressAsync(_customer.Id, 20), Times.Once);
        _shipping.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ShippingQuotesPreserveProviderIdentifierNameAndRate()
    {
        var address = new Address { Id = 20, CountryId = 6 };
        _customers.Setup(x => x.GetCustomerAddressAsync(_customer.Id, address.Id)).ReturnsAsync(address);
        var response = new GetShippingOptionResponse();
        response.ShippingOptions.Add(new ShippingOption
        {
            Name = "Ground",
            ShippingRateComputationMethodSystemName = "Shipping.FixedByWeightByTotal",
            Rate = 9.95m
        });
        _shipping.Setup(x => x.GetShippingOptionsAsync(
                It.Is<IList<ShoppingCartItem>>(cart => cart.Count == 1 && cart[0].StoreId == 3), address, _customer, "", 3))
            .ReturnsAsync(response);

        var result = (OkObjectResult)await _controller.GetShippingOptions(address.Id);

        Assert.That(((ShippingOptionsDto)result.Value!).ShippingOptions,
            Is.EqualTo(new[] { new ShippingQuoteDto("Ground", "Shipping.FixedByWeightByTotal", 9.95m) }));
    }

    [Test]
    public async Task PaymentMethodsUseBillingCountryAndExcludeHiddenButtonAndNonRecurringPlugins()
    {
        _customers.Setup(x => x.GetCustomerAddressAsync(_customer.Id, 21))
            .ReturnsAsync(new Address { Id = 21, CountryId = 6 });
        _payments.Setup(x => x.LoadActivePluginsAsync(_customer, 3, 6)).ReturnsAsync(new List<IPaymentMethod>
        {
            Payment("Eligible", PaymentMethodType.Standard, RecurringPaymentType.Manual),
            Payment("Hidden", PaymentMethodType.Standard, RecurringPaymentType.Manual, hidden: true),
            Payment("Button", PaymentMethodType.Button, RecurringPaymentType.Manual),
            Payment("NonRecurring", PaymentMethodType.Redirection, RecurringPaymentType.NotSupported)
        });

        var result = (OkObjectResult)await _controller.GetPaymentMethods(billingAddressId: 21);
        var methods = (PaymentMethodsDto)result.Value!;

        Assert.That(methods.PaymentMethods.Select(method => method.SystemName), Is.EqualTo(new[] { "Payments.Eligible" }));
    }

    [Test]
    public async Task ShippableCartStillReturnsCandidatesWhenSavedChoicesWouldMakeTheOrderFree()
    {
        _orderProcessing.Setup(x => x.IsPaymentWorkflowRequiredAsync(_cart, false)).ReturnsAsync(false);
        _payments.Setup(x => x.LoadActivePluginsAsync(_customer, 3, 0))
            .ReturnsAsync(new[] { Payment("Eligible", PaymentMethodType.Standard, RecurringPaymentType.Manual) });

        var result = (OkObjectResult)await _controller.GetPaymentMethods();

        Assert.That(((PaymentMethodsDto)result.Value!).PaymentMethods.Select(method => method.SystemName),
            Is.EqualTo(new[] { "Payments.Eligible" }));
        _orderProcessing.Verify(x => x.IsPaymentWorkflowRequiredAsync(_cart, false), Times.Never);
    }

    [Test]
    public async Task NonShippableCartWithDeterminedZeroTotalHasNoPaymentMethods()
    {
        _carts.Setup(x => x.ShoppingCartRequiresShippingAsync(_cart)).ReturnsAsync(false);
        _orderProcessing.Setup(x => x.IsPaymentWorkflowRequiredAsync(_cart, false)).ReturnsAsync(false);

        var result = (OkObjectResult)await _controller.GetPaymentMethods();

        Assert.That(((PaymentMethodsDto)result.Value!).PaymentMethods, Is.Empty);
        _payments.VerifyNoOtherCalls();
    }

    [Test]
    public async Task NonShippableCartWithPricedCheckoutOptionsStillReturnsCandidates()
    {
        _carts.Setup(x => x.ShoppingCartRequiresShippingAsync(_cart)).ReturnsAsync(false);
        _orderProcessing.Setup(x => x.IsPaymentWorkflowRequiredAsync(_cart, false)).ReturnsAsync(false);
        _attributes.Setup(x => x.GetAllAttributesAsync()).ReturnsAsync(new[]
        {
            new CheckoutAttribute { Id = 1, AttributeControlType = AttributeControlType.DropdownList }
        });
        _attributes.Setup(x => x.GetAttributeValuesAsync(1))
            .ReturnsAsync(new[] { new CheckoutAttributeValue { PriceAdjustment = 5 } });
        _payments.Setup(x => x.LoadActivePluginsAsync(_customer, 3, 0))
            .ReturnsAsync(new[] { Payment("Eligible", PaymentMethodType.Standard, RecurringPaymentType.Manual) });

        var result = (OkObjectResult)await _controller.GetPaymentMethods();

        Assert.That(((PaymentMethodsDto)result.Value!).PaymentMethods, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task CountryDisabledStillEnforcesNativeBillingAndShippingEligibility()
    {
        _addressSettings.CountryEnabled = false;
        var address = new Address { Id = 20, CountryId = 6 };
        _customers.Setup(x => x.GetCustomerAddressAsync(_customer.Id, address.Id)).ReturnsAsync(address);
        _countries.Setup(x => x.GetCountryByAddressAsync(address))
            .ReturnsAsync(new Country { Id = 6, AllowsShipping = false, AllowsBilling = false });

        Assert.That(await _controller.GetShippingOptions(address.Id), Is.TypeOf<BadRequestObjectResult>());
        Assert.That(await _controller.GetPaymentMethods(billingAddressId: address.Id), Is.TypeOf<BadRequestObjectResult>());
        _shipping.VerifyNoOtherCalls();
        _payments.VerifyNoOtherCalls();
    }

    [Test]
    public async Task CountryDisabledSkipsPaymentPluginCountryFilterForAnEligibleBillingAddress()
    {
        _addressSettings.CountryEnabled = false;
        var address = new Address { Id = 20, CountryId = 6 };
        _customers.Setup(x => x.GetCustomerAddressAsync(_customer.Id, address.Id)).ReturnsAsync(address);
        _payments.Setup(x => x.LoadActivePluginsAsync(_customer, 3, 0))
            .ReturnsAsync(new[] { Payment("Eligible", PaymentMethodType.Standard, RecurringPaymentType.Manual) });

        var result = (OkObjectResult)await _controller.GetPaymentMethods(billingAddressId: address.Id);

        Assert.That(((PaymentMethodsDto)result.Value!).PaymentMethods, Has.Count.EqualTo(1));
        _countries.Verify(x => x.GetCountryByAddressAsync(address), Times.Once);
        _payments.Verify(x => x.LoadActivePluginsAsync(_customer, 3, 0), Times.Once);
    }

    private static IPaymentMethod Payment(string name, PaymentMethodType type, RecurringPaymentType recurring, bool hidden = false)
    {
        var payment = new Mock<IPaymentMethod>();
        payment.SetupGet(x => x.PaymentMethodType).Returns(type);
        payment.SetupGet(x => x.RecurringPaymentType).Returns(recurring);
        payment.SetupGet(x => x.PluginDescriptor).Returns(new PluginDescriptor { FriendlyName = name, SystemName = $"Payments.{name}" });
        payment.Setup(x => x.HidePaymentMethodAsync(It.IsAny<IList<ShoppingCartItem>>())).ReturnsAsync(hidden);
        return payment.Object;
    }
}
