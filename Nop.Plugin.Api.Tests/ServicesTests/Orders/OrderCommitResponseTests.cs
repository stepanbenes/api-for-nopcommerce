using System.Transactions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Stores;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Plugin.Api.Controllers;
using Nop.Plugin.Api.Delta;
using Nop.Plugin.Api.DTO;
using Nop.Plugin.Api.DTO.Orders;
using Nop.Plugin.Api.Factories;
using Nop.Plugin.Api.Helpers;
using Nop.Plugin.Api.JSON.ActionResults;
using Nop.Plugin.Api.JSON.Serializers;
using Nop.Plugin.Api.Maps;
using Nop.Services.Attributes;
using Nop.Services.Authentication;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Security;
using NUnit.Framework;
using TestAssert = NUnit.Framework.Assert;

namespace Nop.Plugin.Api.Tests.ServicesTests.Orders;

[TestFixture]
[NonParallelizable]
public class OrderCommitResponseTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ReturnsCommittedOrderWhenFollowUpFails(bool detailReadFails)
    {
        var customer = new Customer { Id = 42, Active = true };
        var order = new Order { Id = 73, StoreId = 1, CustomerId = customer.Id };
        var address = new Address { Id = 4 };
        var customers = new Mock<ICustomerService>();
        customers.Setup(x => x.GetCustomerByIdAsync(customer.Id)).ReturnsAsync(customer);
        customers.Setup(x => x.GetCustomerAddressAsync(customer.Id, address.Id)).ReturnsAsync(address);
        var authentication = new Mock<IAuthenticationService>();
        authentication.Setup(x => x.GetAuthenticatedCustomerAsync()).ReturnsAsync(customer);
        var permissions = new Mock<IPermissionService>();
        permissions.Setup(x => x.AuthorizeAsync(StandardPermission.PublicStore.ENABLE_SHOPPING_CART, customer)).ReturnsAsync(true);
        var store = new Mock<IStoreContext>();
        store.Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Store { Id = order.StoreId });
        var cart = new List<ShoppingCartItem> { new() { Id = 8 } };
        var shoppingCart = new Mock<IShoppingCartService>();
        shoppingCart.Setup(x => x.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, order.StoreId, null, null, null, null))
            .ReturnsAsync(cart);
        shoppingCart.Setup(x => x.GetShoppingCartWarningsAsync(cart, string.Empty, true)).ReturnsAsync(new List<string>());
        var placement = new Mock<IOrderProcessingService>();
        placement.Setup(x => x.PlaceOrderAsync(It.IsAny<ProcessPaymentRequest>())).ReturnsAsync(new PlaceOrderResult { PlacedOrder = order });
        var payment = new Mock<IPaymentService>();
        var hadOpenTransactionAfterPlacement = true;
        payment.Setup(x => x.PostProcessPaymentAsync(It.Is<PostProcessPaymentRequest>(r => r.Order == order))).Returns(() =>
        {
            hadOpenTransactionAfterPlacement = Transaction.Current != null;
            return detailReadFails ? Task.CompletedTask : Task.FromException(new InvalidOperationException("private gateway payload"));
        });
        var data = new Mock<INopDataProvider>();
        data.Setup(x => x.CreateTransactionScope()).Returns(() => new TransactionScope(TransactionScopeAsyncFlowOption.Enabled));
        var attributes = new Mock<IAttributeService<CheckoutAttribute, CheckoutAttributeValue>>();
        attributes.Setup(x => x.GetAllAttributesAsync()).ReturnsAsync(new List<CheckoutAttribute>());
        var detail = new Mock<IDTOHelper>();
        if (detailReadFails)
            detail.Setup(x => x.PrepareOrderDTOAsync(order)).ThrowsAsync(new InvalidOperationException("private database detail"));
        else
            detail.Setup(x => x.PrepareOrderDTOAsync(order)).ReturnsAsync(new OrderDto { Id = order.Id, CustomerId = customer.Id });
        var serializer = new Mock<IJsonFieldsSerializer>();
        serializer.Setup(x => x.Serialize(It.IsAny<OrdersRootObject>(), string.Empty))
            .Returns<ISerializableObject, string>((value, _) =>
            {
                var item = ((OrdersRootObject)value).Orders.Single();
                return JsonSerializer.Serialize(new { orders = new[] { new { id = item.Id, full_response = true } } });
            });

        var http = new DefaultHttpContext();
        using var responseBody = new MemoryStream();
        http.Response.Body = responseBody;
        var controller = new OrdersController(null, serializer.Object, null, customers.Object, null, null,
            null, Mock.Of<ICustomerActivityService>(), Mock.Of<ILocalizationService>(), null, new OrderFactory(),
            placement.Object, null, shoppingCart.Object, Mock.Of<IGenericAttributeService>(), store.Object, null,
            null, detail.Object, null, payment.Object, null, permissions.Object, authentication.Object,
            addressService: null, downloadService: null, checkoutAttributeService: attributes.Object, checkoutAttributeParser: null,
            dataProvider: data.Object, logger: NullLogger<OrdersController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        var previous = EngineContext.Current;
        var engine = new Mock<IEngine>();
        engine.Setup(x => x.Resolve<IJsonPropertyMapper>(null)).Returns(new JsonPropertyMapper());
        try
        {
            EngineContext.Replace(engine.Object);
            var delta = new Delta<OrderDto>(new Dictionary<string, object>
            {
                ["customer_id"] = customer.Id
            });
            delta.Dto.BillingAddress = new AddressDto { Id = address.Id };
            var result = await controller.CreateOrder(delta);

            TestAssert.That(hadOpenTransactionAfterPlacement, Is.False);
            TestAssert.That(http.Response.Headers["X-Order-Follow-Up"].ToString(), Does.Contain("/api/orders/73"));
            TestAssert.That(result, Is.TypeOf<RawJsonActionResult>());
            await result.ExecuteResultAsync(controller.ControllerContext);
            var payload = System.Text.Encoding.UTF8.GetString(responseBody.ToArray());
            using var document = JsonDocument.Parse(payload);
            var placedOrder = document.RootElement.GetProperty("orders")[0];
            TestAssert.That(http.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            TestAssert.That(placedOrder.GetProperty("id").GetInt32(), Is.EqualTo(order.Id));
            TestAssert.That(placedOrder.TryGetProperty("full_response", out _), Is.EqualTo(!detailReadFails));
            TestAssert.That(payload, Does.Not.Contain("private gateway payload").And.Not.Contain("private database detail"));
        }
        finally
        {
            EngineContext.Replace(previous);
        }
    }
}
