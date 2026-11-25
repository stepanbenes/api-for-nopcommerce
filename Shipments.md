# Shipments

Use the shipment API to fulfill orders in the current store. The request's store context must match the order's store. Every shipment ID is checked against the order ID in the route. Requests require a bearer token. Reading requires nopCommerce's **Admin area. Shipments. View** permission. Writing requires **Admin area. Shipments. Create, edit, delete**. The API is available to store operators. Vendor accounts use the nopCommerce administration UI for vendor-scoped fulfillment.

| Method and path | Result |
| --- | --- |
| `GET /api/orders/{orderId}/shipments` | The order's shipments and their items |
| `GET /api/orders/{orderId}/shipments/{shipmentId}` | One shipment and its items |
| `POST /api/orders/{orderId}/shipments` | A new shipment and its items, with HTTP 201 |
| `PUT /api/orders/{orderId}/shipments/{shipmentId}` | Updated tracking number and internal admin comment |
| `POST /api/orders/{orderId}/shipments/{shipmentId}/ship` | Marks a delivery shipment as shipped |
| `POST /api/orders/{orderId}/shipments/{shipmentId}/ready-for-pickup` | Marks a pickup shipment as ready |
| `POST /api/orders/{orderId}/shipments/{shipmentId}/deliver` | Marks a shipped or ready-for-pickup shipment as delivered |

## Create a shipment

Send a JSON object with one entry for each order item going into this shipment.

```json
{
  "tracking_number": "123456789",
  "admin_comment": "Packed in two cartons",
  "items": [
    { "order_item_id": 42, "quantity": 2 }
  ]
}
```

Use item IDs from `GET /api/orders/{orderId}/items`. Quantities must be positive and can be smaller than the outstanding order quantity. nopCommerce totals quantities across existing shipments, including shipments that have not yet shipped. A request that exceeds the remaining quantity returns HTTP 409. Concurrent shipment API writes are handled with serializable database transactions. On SQL Server, shipment writes return HTTP 503 when `DataConfig.WithNoLock` is enabled. The nopCommerce default is disabled. After HTTP 409, reload the order's shipments and retry only with quantities that are still outstanding.

For stock-managed products configured with multiple warehouses, include a `warehouse_id` from the product's warehouse inventory. For other products, omit it to use the warehouse configured on the product. The response includes item IDs, quantities, warehouse IDs, weight, and shipment timestamps.

## Update tracking and fulfillment state

To update tracking, send `{"tracking_number":"123456789"}` to the shipment's PUT route. Omitted metadata is preserved. Send an empty string to clear a field. The admin comment is visible only to operators with shipment access.

The transition routes use nopCommerce's fulfillment services to update inventory, order state, and order notes. For delivery shipments, call `ship` before `deliver`. For pickup shipments, call `ready-for-pickup` before `deliver`. A transition that is unavailable in the current state returns HTTP 409. Add `?notify_customer=true` when you want nopCommerce to queue its standard customer notification.
