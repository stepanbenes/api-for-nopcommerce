using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Nop.Plugin.Api.DTOs.Shipments;

public class ShipmentMetadata
{
    [StringLength(400)]
    [JsonProperty("tracking_number")]
    public string TrackingNumber { get; set; }

    [JsonProperty("admin_comment")]
    public string AdminComment { get; set; }
}

public class CreateShipmentRequest : ShipmentMetadata
{
    [Required, MinLength(1)]
    [JsonProperty("items")]
    public List<CreateShipmentItem> Items { get; set; }
}

public class CreateShipmentItem
{
    [Range(1, int.MaxValue)]
    [JsonProperty("order_item_id")]
    public int OrderItemId { get; set; }

    [Range(1, int.MaxValue)]
    [JsonProperty("quantity")]
    public int Quantity { get; set; }

    [Range(0, int.MaxValue)]
    [JsonProperty("warehouse_id")]
    public int? WarehouseId { get; set; }
}

public class ShipmentItemDto
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("order_item_id")]
    public int OrderItemId { get; set; }

    [JsonProperty("quantity")]
    public int Quantity { get; set; }

    [JsonProperty("warehouse_id")]
    public int WarehouseId { get; set; }
}

public class ShipmentDto : ShipmentMetadata
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("order_id")]
    public int OrderId { get; set; }

    [JsonProperty("total_weight")]
    public decimal? TotalWeight { get; set; }

    [JsonProperty("created_on_utc")]
    public DateTime CreatedOnUtc { get; set; }

    [JsonProperty("shipped_date_utc")]
    public DateTime? ShippedDateUtc { get; set; }

    [JsonProperty("ready_for_pickup_date_utc")]
    public DateTime? ReadyForPickupDateUtc { get; set; }

    [JsonProperty("delivery_date_utc")]
    public DateTime? DeliveryDateUtc { get; set; }

    [JsonProperty("items")]
    public List<ShipmentItemDto> Items { get; set; }
}

public class ShipmentsRootObject
{
    [JsonProperty("shipments")]
    public List<ShipmentDto> Shipments { get; set; }
}
