using System.Data.Common;
using System.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Events;
using Nop.Data;
using Nop.Plugin.Api.Attributes;
using Nop.Plugin.Api.Authorization.Attributes;
using Nop.Plugin.Api.DTO.Errors;
using Nop.Plugin.Api.DTOs.Shipments;
using Nop.Services.Authentication;
using Nop.Services.Catalog;

using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Services.Shipping;

namespace Nop.Plugin.Api.Controllers;

[ApiController]
[Authorize(Policy = JwtBearerDefaults.AuthenticationScheme, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Route("api/orders/{orderId:int}/shipments")]
public class ShipmentsController(
    IStoreContext storeContext,
    IAuthenticationService authenticationService,
    IOrderService orderService,
    IShipmentService shipmentService,
    IProductService productService,
    IWarehouseService warehouseService,
    IOrderProcessingService orderProcessingService,
    INopDataProvider dataProvider,
    IRepository<Shipment> shipmentRepository,
    IRepository<ShipmentItem> shipmentItemRepository,
    IEventPublisher eventPublisher,
    ILogger<ShipmentsController> logger) : ControllerBase
{
    [HttpGet(Name = "GetOrderShipments")]
    [ApiAudience(ApiAudience.BackOffice)]
    [AuthorizePermission(StandardPermission.Orders.SHIPMENTS_VIEW)]
    [ProducesResponseType(typeof(ShipmentsRootObject), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetOrderShipments(int orderId)
    {
        if (!await IsStoreOperatorAsync())
            return StatusCode(403);
        if (await GetStoreOrderAsync(orderId) == null)
            return NotFound();

        var shipments = await shipmentService.GetShipmentsByOrderIdAsync(orderId);
        var items = (await OrderShipmentItems(orderId).ToListAsync()).ToLookup(item => item.ShipmentId);
        return Ok(new ShipmentsRootObject
        {
            Shipments = shipments.OrderBy(shipment => shipment.Id)
                .Select(shipment => ToDto(shipment, items[shipment.Id])).ToList()
        });
    }

    [HttpGet("{shipmentId:int}", Name = "GetOrderShipment")]
    [ApiAudience(ApiAudience.BackOffice)]
    [AuthorizePermission(StandardPermission.Orders.SHIPMENTS_VIEW)]
    [ProducesResponseType(typeof(ShipmentDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetOrderShipment(int orderId, int shipmentId)
    {
        if (!await IsStoreOperatorAsync())
            return StatusCode(403);
        if (await GetStoreOrderAsync(orderId) == null)
            return NotFound();

        var shipment = await GetOrderShipmentAsync(orderId, shipmentId);
        return shipment == null ? NotFound() : Ok(await ToDtoAsync(shipment));
    }

    [HttpPost(Name = "CreateOrderShipment")]
    [ApiAudience(ApiAudience.BackOffice)]
    [AuthorizePermission(StandardPermission.Orders.SHIPMENTS_CREATE_EDIT_DELETE)]
    [ProducesResponseType(typeof(ShipmentDto), 201)]
    [ProducesResponseType(typeof(ErrorsRootObject), 400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(ErrorsRootObject), 409)]
    [ProducesResponseType(typeof(ErrorsRootObject), 503)]
    public Task<IActionResult> CreateOrderShipment(int orderId, [FromBody] CreateShipmentRequest request)
    {
        return WriteAsync(orderId, async (order, afterCommit) =>
        {
            if (order.OrderStatus == OrderStatus.Cancelled || order.ShippingStatus == ShippingStatus.ShippingNotRequired)
                return ShipmentError(HttpStatusCode.Conflict, "order", "The order cannot be shipped.");

            if (request.Items.Any(item => item == null) ||
                request.Items.Select(item => item.OrderItemId).Distinct().Count() != request.Items.Count)
                return ShipmentError(HttpStatusCode.BadRequest, "items", "Supply each order item once per shipment.");

            var orderItems = (await orderService.GetOrderItemsAsync(order.Id, isShipEnabled: true))
                .ToDictionary(item => item.Id);
            var allocated = (await OrderShipmentItems(order.Id)
                .GroupBy(item => item.OrderItemId)
                .Select(group => new { OrderItemId = group.Key, Quantity = group.Sum(item => (long)item.Quantity) })
                .ToListAsync()).ToDictionary(item => item.OrderItemId, item => item.Quantity);
            var items = new List<ShipmentItem>();
            decimal? totalWeight = null;
            foreach (var item in request.Items)
            {
                if (!orderItems.TryGetValue(item.OrderItemId, out var orderItem))
                    return ShipmentError(HttpStatusCode.BadRequest, "items", $"Order item {item.OrderItemId} is outside this order or cannot be shipped.");

                if (item.Quantity > orderItem.Quantity - allocated.GetValueOrDefault(orderItem.Id))
                    return ShipmentError(HttpStatusCode.Conflict, "quantity", $"Order item {item.OrderItemId} has fewer units left to fulfill.");

                var product = await productService.GetProductByIdAsync(orderItem.ProductId);
                if (product == null)
                    return ShipmentError(HttpStatusCode.BadRequest, "items", $"Product for order item {item.OrderItemId} is unavailable.");

                var warehouseId = await ResolveWarehouseAsync(product, item.WarehouseId);
                if (!warehouseId.HasValue)
                    return ShipmentError(HttpStatusCode.BadRequest, "warehouse_id", $"Warehouse is invalid for order item {item.OrderItemId}.");

                if (orderItem.ItemWeight.HasValue)
                    totalWeight = (totalWeight ?? 0) + orderItem.ItemWeight.Value * item.Quantity;

                items.Add(new ShipmentItem { OrderItemId = orderItem.Id, Quantity = item.Quantity, WarehouseId = warehouseId.Value });
            }

            var shipment = new Shipment
            {
                OrderId = order.Id,
                TrackingNumber = request.TrackingNumber,
                AdminComment = request.AdminComment,
                TotalWeight = totalWeight,
                CreatedOnUtc = DateTime.UtcNow
            };
            await shipmentService.InsertShipmentAsync(shipment);
            foreach (var item in items)
            {
                item.ShipmentId = shipment.Id;
                await shipmentService.InsertShipmentItemAsync(item);
            }

            await orderService.InsertOrderNoteAsync(new OrderNote
            {
                OrderId = order.Id, Note = $"Shipment# {shipment.Id} was added.", CreatedOnUtc = DateTime.UtcNow
            });
            afterCommit(async () =>
            {
                await eventPublisher.PublishAsync(new ShipmentCreatedEvent(shipment));
                if (!string.IsNullOrWhiteSpace(shipment.TrackingNumber))
                    await eventPublisher.PublishAsync(new ShipmentTrackingNumberSetEvent(shipment));
            });

            var response = ToDto(shipment, items);
            return CreatedAtRoute("GetOrderShipment", new { orderId, shipmentId = shipment.Id }, response);
        });
    }

    [HttpPut("{shipmentId:int}", Name = "UpdateOrderShipment")]
    [ApiAudience(ApiAudience.BackOffice)]
    [AuthorizePermission(StandardPermission.Orders.SHIPMENTS_CREATE_EDIT_DELETE)]
    [ProducesResponseType(typeof(ShipmentDto), 200)]
    [ProducesResponseType(typeof(ErrorsRootObject), 400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(ErrorsRootObject), 409)]
    [ProducesResponseType(typeof(ErrorsRootObject), 503)]
    public Task<IActionResult> UpdateOrderShipment(int orderId, int shipmentId, [FromBody] ShipmentMetadata request)
    {
        return WriteAsync(orderId, async (order, afterCommit) =>
        {
            var shipment = await GetOrderShipmentAsync(order.Id, shipmentId);
            if (shipment == null)
                return NotFound();

            // Omission preserves stored metadata. An empty string clears the field.
            var trackingChanged = request.TrackingNumber != null && shipment.TrackingNumber != request.TrackingNumber;
            if (trackingChanged)
                shipment.TrackingNumber = request.TrackingNumber;
            if (request.AdminComment != null)
                shipment.AdminComment = request.AdminComment;

            await shipmentService.UpdateShipmentAsync(shipment);
            if (trackingChanged)
                afterCommit(() => eventPublisher.PublishAsync(new ShipmentTrackingNumberSetEvent(shipment)));

            return Ok(await ToDtoAsync(shipment));
        });
    }

    [HttpPost("{shipmentId:int}/ship", Name = "ShipOrderShipment")]
    [ApiAudience(ApiAudience.BackOffice)]
    [AuthorizePermission(StandardPermission.Orders.SHIPMENTS_CREATE_EDIT_DELETE)]
    [ProducesResponseType(typeof(ShipmentDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(ErrorsRootObject), 409)]
    [ProducesResponseType(typeof(ErrorsRootObject), 503)]
    public Task<IActionResult> ShipOrderShipment(int orderId, int shipmentId, [FromQuery(Name = "notify_customer")] bool notifyCustomer = false)
    {
        return TransitionAsync(orderId, shipmentId, (order, shipment) => !order.PickupInStore &&
            !shipment.ShippedDateUtc.HasValue && !shipment.DeliveryDateUtc.HasValue,
            shipment => orderProcessingService.ShipAsync(shipment, notifyCustomer));
    }

    [HttpPost("{shipmentId:int}/ready-for-pickup", Name = "MarkOrderShipmentReadyForPickup")]
    [ApiAudience(ApiAudience.BackOffice)]
    [AuthorizePermission(StandardPermission.Orders.SHIPMENTS_CREATE_EDIT_DELETE)]
    [ProducesResponseType(typeof(ShipmentDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(ErrorsRootObject), 409)]
    [ProducesResponseType(typeof(ErrorsRootObject), 503)]
    public Task<IActionResult> MarkOrderShipmentReadyForPickup(int orderId, int shipmentId, [FromQuery(Name = "notify_customer")] bool notifyCustomer = false)
    {
        return TransitionAsync(orderId, shipmentId, (order, shipment) => order.PickupInStore &&
            !shipment.ReadyForPickupDateUtc.HasValue && !shipment.DeliveryDateUtc.HasValue,
            shipment => orderProcessingService.ReadyForPickupAsync(shipment, notifyCustomer));
    }

    [HttpPost("{shipmentId:int}/deliver", Name = "DeliverOrderShipment")]
    [ApiAudience(ApiAudience.BackOffice)]
    [AuthorizePermission(StandardPermission.Orders.SHIPMENTS_CREATE_EDIT_DELETE)]
    [ProducesResponseType(typeof(ShipmentDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(ErrorsRootObject), 409)]
    [ProducesResponseType(typeof(ErrorsRootObject), 503)]
    public Task<IActionResult> DeliverOrderShipment(int orderId, int shipmentId, [FromQuery(Name = "notify_customer")] bool notifyCustomer = false)
    {
        return TransitionAsync(orderId, shipmentId, (order, shipment) => !shipment.DeliveryDateUtc.HasValue &&
            (order.PickupInStore ? shipment.ReadyForPickupDateUtc.HasValue : shipment.ShippedDateUtc.HasValue),
            shipment => orderProcessingService.DeliverAsync(shipment, notifyCustomer));
    }

    private Task<IActionResult> TransitionAsync(int orderId, int shipmentId,
        Func<Order, Shipment, bool> canTransition, Func<Shipment, Task> transition)
    {
        return WriteAsync(orderId, async (order, _) =>
        {
            var shipment = await GetOrderShipmentAsync(order.Id, shipmentId);
            if (shipment == null)
                return NotFound();

            if (order.OrderStatus == OrderStatus.Cancelled || !canTransition(order, shipment))
                return ShipmentError(HttpStatusCode.Conflict, "shipment", "The shipment cannot make that transition in its current state.");

            await transition(shipment);
            return Ok(await ToDtoAsync(shipment));
        });
    }

    private async Task<IActionResult> WriteAsync(int orderId, Func<Order, Action<Func<Task>>, Task<IActionResult>> write)
    {
        if (!await IsStoreOperatorAsync())
            return StatusCode(403);

        if (DataSettingsManager.LoadSettings() is { DataProvider: DataProviderType.SqlServer, WithNoLock: true })
            return ShipmentError(HttpStatusCode.ServiceUnavailable, "shipment", "Shipment writes require SQL Server DataConfig.WithNoLock to be disabled.");

        IActionResult result;
        Func<Task> afterCommit = null;
        try
        {
            // Native serializable isolation protects warehouse allocation and order state reads.
            using var transaction = dataProvider.CreateTransactionScope();
            var order = await GetStoreOrderAsync(orderId);
            if (order == null)
                return NotFound();

            result = await write(order, action => afterCommit = action);
            if (result is OkObjectResult or CreatedAtRouteResult)
                transaction.Complete();
        }
        catch (Exception exception) when (IsConcurrentWrite(exception))
        {
            return ShipmentError(HttpStatusCode.Conflict, "shipment", "A concurrent fulfillment changed the order. Reload the shipment state before retrying.");
        }

        if (afterCommit != null)
        {
            try
            {
                await afterCommit();
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Post-commit shipment event dispatch failed for order {OrderId}.", orderId);
            }
        }

        return result;
    }

    private IQueryable<ShipmentItem> OrderShipmentItems(int orderId)
    {
        return from item in shipmentItemRepository.Table
               join shipment in shipmentRepository.Table on item.ShipmentId equals shipment.Id
               where shipment.OrderId == orderId
               select item;
    }

    private static bool IsConcurrentWrite(Exception exception)
    {
        for (var cause = exception; cause != null; cause = cause.InnerException)
        {
            if (cause is DbException { SqlState: "40001" or "40P01" } ||
                cause is Microsoft.Data.SqlClient.SqlException { Number: 1205 or 3960 } ||
                cause is MySqlConnector.MySqlException { Number: 1205 or 1213 })
                return true;
        }

        return false;
    }

    private async Task<bool> IsStoreOperatorAsync()
    {
        var customer = await authenticationService.GetAuthenticatedCustomerAsync();
        return customer is { VendorId: 0 };
    }

    private async Task<Order> GetStoreOrderAsync(int orderId)
    {
        var order = await orderService.GetOrderByIdAsync(orderId);
        if (order == null || order.Deleted || order.StoreId != (await storeContext.GetCurrentStoreAsync()).Id)
            return null;

        return order;
    }

    private async Task<Shipment> GetOrderShipmentAsync(int orderId, int shipmentId)
    {
        var shipment = await shipmentService.GetShipmentByIdAsync(shipmentId);
        return shipment?.OrderId == orderId ? shipment : null;
    }

    private async Task<int?> ResolveWarehouseAsync(Product product, int? requestedId)
    {
        if (product.ManageInventoryMethod != ManageInventoryMethod.ManageStock || !product.UseMultipleWarehouses)
            return requestedId == null || requestedId == product.WarehouseId ? product.WarehouseId : null;

        if (requestedId is not > 0 || await warehouseService.GetWarehouseByIdAsync(requestedId.Value) == null)
            return null;

        var inventory = await productService.GetAllProductWarehouseInventoryRecordsAsync(product.Id);
        return inventory.Any(record => record.WarehouseId == requestedId.Value) ? requestedId : null;
    }

    private async Task<ShipmentDto> ToDtoAsync(Shipment shipment) =>
        ToDto(shipment, await shipmentService.GetShipmentItemsByShipmentIdAsync(shipment.Id));

    private static ShipmentDto ToDto(Shipment shipment, IEnumerable<ShipmentItem> items)
    {
        return new ShipmentDto
        {
            Id = shipment.Id,
            OrderId = shipment.OrderId,
            TrackingNumber = shipment.TrackingNumber,
            AdminComment = shipment.AdminComment,
            TotalWeight = shipment.TotalWeight,
            CreatedOnUtc = shipment.CreatedOnUtc,
            ShippedDateUtc = shipment.ShippedDateUtc,
            ReadyForPickupDateUtc = shipment.ReadyForPickupDateUtc,
            DeliveryDateUtc = shipment.DeliveryDateUtc,
            Items = items.OrderBy(item => item.Id).Select(item => new ShipmentItemDto
            {
                Id = item.Id, OrderItemId = item.OrderItemId, Quantity = item.Quantity, WarehouseId = item.WarehouseId
            }).ToList()
        };
    }

    private ObjectResult ShipmentError(HttpStatusCode status, string field, string message)
    {
        return StatusCode((int)status, new ErrorsRootObject
        {
            Errors = new Dictionary<string, List<string>> { [field] = new() { message } }
        });
    }
}
