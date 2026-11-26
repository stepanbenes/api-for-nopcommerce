using Newtonsoft.Json;

namespace Nop.Plugin.Api.DTO.Products;

public sealed record BulkPriceStockResponse(
    [property: JsonProperty("results", Required = Required.Always)] List<BulkPriceStockResult> Results);

public sealed record BulkPriceStockResult(
    [property: JsonProperty("product_id", Required = Required.Always)] int ProductId,
    [property: JsonProperty("status", Required = Required.Always)] string Status);
