using System.Transactions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Events;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Data.Configuration;
using Nop.Plugin.Api.Controllers;
using Nop.Plugin.Api.DTOs.Shipments;
using Nop.Services.Authentication;
using Nop.Services.Orders;
using Nop.Services.Shipping;
using NUnit.Framework;
using TestAssert = NUnit.Framework.Assert;

namespace Nop.Plugin.Api.Tests.ServicesTests.Shipments;

public class ShipmentScopeTests
{
    [Test]
    public async Task VendorCannotReadStoreShipments()
    {
        var customer = new Customer { VendorId = 5 };
        var (controller, shipmentService) = CreateController(customer, orderStoreId: 1);
        var result = await controller.GetOrderShipments(2);

        TestAssert.That(result, Is.TypeOf<StatusCodeResult>());
        TestAssert.That(((StatusCodeResult)result).StatusCode, Is.EqualTo(403));
        shipmentService.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AnotherStoreOrderCannotBeRead()
    {
        var (controller, shipmentService) = CreateController(new Customer(), orderStoreId: 4);
        var result = await controller.GetOrderShipments(2);

        TestAssert.That(result, Is.TypeOf<NotFoundResult>());
        shipmentService.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ShipmentMustBelongToTheOrderInTheRoute()
    {
        var (controller, shipmentService) = CreateController(new Customer(), orderStoreId: 1);
        shipmentService.Setup(service => service.GetShipmentByIdAsync(7))
            .ReturnsAsync(new Shipment { Id = 7, OrderId = 3 });

        var result = await controller.GetOrderShipment(2, 7);

        TestAssert.That(result, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    [NonParallelizable]
    public async Task WriteRejectsDirtyReadsOnSqlServer()
    {
        var previous = Singleton<DataConfig>.Instance;
        Singleton<DataConfig>.Instance = new DataConfig { DataProvider = DataProviderType.SqlServer, WithNoLock = true };
        try
        {
            var (controller, shipmentService) = CreateController(new Customer(), orderStoreId: 1);
            var result = await controller.UpdateOrderShipment(2, 7, new ShipmentMetadata { TrackingNumber = "new" });
            TestAssert.That(result, Is.TypeOf<ObjectResult>());
            TestAssert.That(((ObjectResult)result).StatusCode, Is.EqualTo(503));
            shipmentService.VerifyNoOtherCalls();
        }
        finally
        {
            Singleton<DataConfig>.Instance = previous;
        }
    }

    [Test]
    [NonParallelizable]
    public async Task TrackingEventFailureAfterCommitPreservesSuccessfulResponse()
    {
        var previous = Singleton<DataConfig>.Instance;
        Singleton<DataConfig>.Instance = new DataConfig { DataProvider = DataProviderType.PostgreSQL };
        try
        {
            var provider = new Mock<INopDataProvider>();
            provider.Setup(service => service.CreateTransactionScope())
                .Returns(() => new TransactionScope(TransactionScopeAsyncFlowOption.Enabled));
            var publisher = new Mock<IEventPublisher>();
            var eventOutsideTransaction = false;
            publisher.Setup(service => service.PublishAsync(It.IsAny<ShipmentTrackingNumberSetEvent>()))
                .Returns(() =>
                {
                    eventOutsideTransaction = Transaction.Current == null;
                    return Task.FromException(new InvalidOperationException("Simulated notification failure"));
                });
            var (controller, shipmentService) = CreateController(new Customer(), orderStoreId: 1, provider.Object, publisher.Object);
            shipmentService.Setup(service => service.GetShipmentByIdAsync(7))
                .ReturnsAsync(new Shipment { Id = 7, OrderId = 2, TrackingNumber = "old" });
            shipmentService.Setup(service => service.GetShipmentItemsByShipmentIdAsync(7))
                .ReturnsAsync(new List<ShipmentItem>());

            var result = await controller.UpdateOrderShipment(2, 7, new ShipmentMetadata { TrackingNumber = "new" });

            TestAssert.That(result, Is.TypeOf<OkObjectResult>());
            TestAssert.That(((ShipmentDto)((OkObjectResult)result).Value).TrackingNumber, Is.EqualTo("new"));
            TestAssert.That(eventOutsideTransaction, Is.True);
            shipmentService.Verify(service => service.UpdateShipmentAsync(It.Is<Shipment>(shipment => shipment.TrackingNumber == "new")), Times.Once);
        }
        finally
        {
            Singleton<DataConfig>.Instance = previous;
        }
    }

    private static (ShipmentsController Controller, Mock<IShipmentService> ShipmentService) CreateController(Customer customer, int orderStoreId,
        INopDataProvider provider = null, IEventPublisher publisher = null)
    {
        var store = new Mock<IStoreContext>();
        store.Setup(context => context.GetCurrentStoreAsync()).ReturnsAsync(new Core.Domain.Stores.Store { Id = 1 });
        var authentication = new Mock<IAuthenticationService>();
        authentication.Setup(service => service.GetAuthenticatedCustomerAsync()).ReturnsAsync(customer);
        var orders = new Mock<IOrderService>();
        orders.Setup(service => service.GetOrderByIdAsync(2)).ReturnsAsync(new Order { Id = 2, StoreId = orderStoreId });
        var shipments = new Mock<IShipmentService>();
        return (new ShipmentsController(store.Object, authentication.Object, orders.Object, shipments.Object,
            null, null, null, provider, null, null, publisher, NullLogger<ShipmentsController>.Instance), shipments);
    }
}
