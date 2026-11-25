using NUnit.Framework;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Api.Authorization.Policies;
using Nop.Plugin.Api.Authorization.Requirements;
using Nop.Plugin.Api.Controllers;
using Nop.Plugin.Api.JSON.Serializers;
using Nop.Plugin.Api.Models.ShoppingCartsParameters;
using Nop.Services.Authentication;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Stores;
using Nop.Data;
using Nop.Plugin.Api.Services;
using Nop.Services.Customers;

namespace Nop.Plugin.Api.Tests.ServicesTests.ShoppingCartItems;

[TestFixture]
public class CartAuthorizationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task CartQueriesHonorStoreSharingAndPreserveCustomerAndTypeFilters(bool shared)
    {
        var currentItem = new ShoppingCartItem { Id = 1, StoreId = 2, CustomerId = 3, ShoppingCartType = ShoppingCartType.ShoppingCart };
        var otherStoreItem = new ShoppingCartItem { Id = 2, StoreId = 4, CustomerId = 3, ShoppingCartType = ShoppingCartType.ShoppingCart };
        var wishlistItem = new ShoppingCartItem { Id = 3, StoreId = 4, CustomerId = 3, ShoppingCartType = ShoppingCartType.Wishlist };
        var otherCustomerItem = new ShoppingCartItem { Id = 4, StoreId = 4, CustomerId = 5, ShoppingCartType = ShoppingCartType.ShoppingCart };
        var repository = new Mock<IRepository<ShoppingCartItem>>();
        repository.SetupGet(x => x.Table).Returns(new[] { currentItem, otherStoreItem, wishlistItem, otherCustomerItem }.AsQueryable());
        var store = new Mock<IStoreContext>();
        store.Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Store { Id = 2 });
        var service = new ShoppingCartItemApiService(repository.Object, store.Object,
            new ShoppingCartSettings { CartsSharedBetweenStores = shared });

        var expected = shared ? new[] { currentItem, otherStoreItem } : new[] { currentItem };
        Assert.That(await service.GetShoppingCartItemsAsync(3, shoppingCartType: ShoppingCartType.ShoppingCart),
            Is.EqualTo(expected));
        Assert.That(await service.GetShoppingCartItemsForDeletionAsync(3, ShoppingCartType.ShoppingCart, new[] { 1, 2, 3, 4 }),
            Is.EqualTo(expected));
        Assert.That(await service.GetShoppingCartItemAsync(1, 3), Is.SameAs(currentItem));
        Assert.That(await service.GetShoppingCartItemAsync(2, 3), Is.SameAs(shared ? otherStoreItem : null));
        Assert.That(await service.GetShoppingCartItemAsync(2), Is.SameAs(shared ? otherStoreItem : null));
        Assert.That(await service.GetShoppingCartItemAsync(4, 3), Is.Null);
    }

    [TestCase(false, 0)]
    [TestCase(true, 2)]
    public async Task BulkDeletionRequiresEveryRequestedItemBeforeDeleting(bool complete, int expectedDeletes)
    {
        var customer = new Customer { Id = 3 };
        var authentication = new Mock<Nop.Services.Authentication.IAuthenticationService>();
        authentication.Setup(x => x.GetAuthenticatedCustomerAsync()).ReturnsAsync(customer);
        var permissions = new Mock<IPermissionService>();
        permissions.Setup(x => x.AuthorizeAsync(StandardPermission.PublicStore.ENABLE_SHOPPING_CART, customer)).ReturnsAsync(true);
        var resolvedItems = new List<ShoppingCartItem> { new() { Id = 1, CustomerId = 3, StoreId = 2 } };
        if (complete)
            resolvedItems.Add(new ShoppingCartItem { Id = 2, CustomerId = 3, StoreId = 2 });

        var items = new Mock<IShoppingCartItemApiService>();
        items.Setup(x => x.GetShoppingCartItemsForDeletionAsync(customer.Id, ShoppingCartType.ShoppingCart,
            It.Is<int[]>(ids => ids.SequenceEqual(new[] { 1, 2 })))).ReturnsAsync(resolvedItems);
        var shoppingCart = new Mock<IShoppingCartService>();
        var controller = new ShoppingCartItemsController(items.Object, Mock.Of<IJsonFieldsSerializer>(),
            null, null, null, null, null, Mock.Of<ICustomerActivityService>(), Mock.Of<ILocalizationService>(),
            shoppingCart.Object, null, null, null, null, null, null, permissions.Object, authentication.Object);
        var parameters = new ShoppingCartItemsDeleteParametersModel
        {
            ShoppingCartType = Nop.Plugin.Api.DTOs.ShoppingCarts.ShoppingCartType.ShoppingCart,
            Ids = new List<int> { 1, 2, 2 }
        };

        var result = await controller.DeleteShoppingCartItems(parameters);

        if (complete)
            Assert.That(result, Is.TypeOf<OkResult>());
        else
        {
            var httpContext = new DefaultHttpContext();
            await result.ExecuteResultAsync(new ActionContext { HttpContext = httpContext });
            Assert.That(httpContext.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
        }

        shoppingCart.Verify(x => x.DeleteShoppingCartItemAsync(It.IsAny<ShoppingCartItem>(), true, false),
            Times.Exactly(expectedDeletes));
    }

    [TestCase(true, true, false, false)]
    [TestCase(false, false, false, false)]
    [TestCase(false, false, true, false)]
    [TestCase(false, false, false, true)]
    public async Task BearerResolutionDoesNotConsultCookies(bool accepted, bool active, bool deleted, bool requireReLogin)
    {
        var customer = new Customer
        {
            CustomerGuid = Guid.NewGuid(), Active = active, Deleted = deleted, RequireReLogin = requireReLogin
        };
        var customers = new Mock<ICustomerService>();
        customers.Setup(x => x.GetCustomerByGuidAsync(customer.CustomerGuid)).ReturnsAsync(customer);
        var authentication = new Mock<Microsoft.AspNetCore.Authentication.IAuthenticationService>(MockBehavior.Strict);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer test";
        context.RequestServices = Mock.Of<IServiceProvider>(x =>
            x.GetService(typeof(Microsoft.AspNetCore.Authentication.IAuthenticationService)) == authentication.Object);
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, customer.CustomerGuid.ToString()) }, "Bearer");
        authentication.Setup(x => x.AuthenticateAsync(context, JwtBearerDefaults.AuthenticationScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Bearer")));
        var accessor = new HttpContextAccessor { HttpContext = context };
        var service = new BearerTokenOrCookieAuthenticationService(new CustomerSettings(), customers.Object, accessor);
        customers.Setup(x => x.GetCustomerRolesAsync(customer, false)).ReturnsAsync(new List<CustomerRole>
        {
            new() { SystemName = Nop.Plugin.Api.Infrastructure.Constants.Roles.ApiRoleSystemName }
        });
        var requirement = new CustomerRoleRequirement();
        var authorization = new AuthorizationHandlerContext(new[] { requirement }, new ClaimsPrincipal(), context);
        await new CustomerRoleAuthorizationPolicy(service, customers.Object, accessor).HandleAsync(authorization);

        Assert.That(authorization.HasSucceeded, Is.EqualTo(accepted));
        Assert.That(authorization.HasFailed, Is.EqualTo(!accepted));
        Assert.That(await service.GetAuthenticatedCustomerAsync(), Is.SameAs(accepted ? customer : null));
        customers.Verify(x => x.GetCustomerByGuidAsync(customer.CustomerGuid), Times.Once);
        accessor.HttpContext = null;
    }

    [Test]
    public async Task CookieOnlyRequestUsesHostCustomerAuthentication()
    {
        var customer = new Customer { Id = 3, Email = "customer@example.test", Active = true };
        var customers = new Mock<ICustomerService>();
        customers.Setup(x => x.GetCustomerByEmailAsync(customer.Email)).ReturnsAsync(customer);
        customers.Setup(x => x.IsRegisteredAsync(customer, true)).ReturnsAsync(true);
        customers.Setup(x => x.GetCurrentPasswordAsync(customer.Id)).ReturnsAsync(new CustomerPassword());

        var authentication = new Mock<Microsoft.AspNetCore.Authentication.IAuthenticationService>(MockBehavior.Strict);
        var context = new DefaultHttpContext();
        context.RequestServices = Mock.Of<IServiceProvider>(x =>
            x.GetService(typeof(Microsoft.AspNetCore.Authentication.IAuthenticationService)) == authentication.Object);
        var scheme = NopAuthenticationDefaults.AuthenticationScheme;
        var claim = new Claim(ClaimTypes.Email, customer.Email, ClaimValueTypes.String, NopAuthenticationDefaults.ClaimsIssuer);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { claim }, scheme));
        authentication.Setup(x => x.AuthenticateAsync(context, scheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, scheme)));
        var accessor = new HttpContextAccessor { HttpContext = context };
        var service = new BearerTokenOrCookieAuthenticationService(new CustomerSettings(), customers.Object, accessor);

        Assert.That(await service.GetAuthenticatedCustomerAsync(), Is.SameAs(customer));
        accessor.HttpContext = null;
    }
}
