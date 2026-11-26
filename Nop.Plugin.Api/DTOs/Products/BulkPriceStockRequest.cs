using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Nop.Plugin.Api.DTO.Products;

public sealed class BulkPriceStockRequest : IValidatableObject
{
    [JsonProperty("operation_id", Required = Required.Always)]
    public Guid OperationId { get; init; }

    [JsonProperty("version", Required = Required.Always)]
    [JsonConverter(typeof(BulkPriceStockVersionConverter))]
    [Range(typeof(long), "1", "9223372036854775807")]
    public long Version { get; init; }

    [JsonProperty("items", Required = Required.Always)]
    [MinLength(1), MaxLength(100)]
    public List<BulkPriceStockItem> Items { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OperationId == Guid.Empty)
            yield return new ValidationResult("Supply a non-empty operation ID.", ["operation_id"]);

        if (Items.Contains(null))
            yield return new ValidationResult("Items cannot contain null.", ["items"]);
        else if (Items.Select(item => item.ProductId).Distinct().Count() != Items.Count)
            yield return new ValidationResult("Each product ID must appear only once.", ["items"]);
    }
}

internal sealed class BulkPriceStockVersionConverter : JsonConverter<long>
{
    public override bool CanWrite => false;

    public override long ReadJson(JsonReader reader, Type objectType, long existingValue, bool hasExistingValue,
        JsonSerializer serializer) =>
        reader.TokenType == JsonToken.Integer && reader.Value is long version
            ? version
            : throw new JsonSerializationException("Supply an integer version within the signed 64-bit range.");

    public override void WriteJson(JsonWriter writer, long value, JsonSerializer serializer) =>
        throw new NotSupportedException();
}

public sealed class BulkPriceStockItem : IValidatableObject
{
    [JsonProperty("product_id", Required = Required.Always)]
    [Range(1, int.MaxValue)]
    public int ProductId { get; init; }

    [JsonProperty("price")]
    [Range(typeof(decimal), "0", "99999999999999.9999", ParseLimitsInInvariantCulture = true)]
    public decimal? Price { get; init; }

    [JsonProperty("stock_quantity")]
    [Range(0, int.MaxValue)]
    public int? StockQuantity { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Price.HasValue && !StockQuantity.HasValue)
            yield return new ValidationResult("Supply at least one of price or stock_quantity.", ["price", "stock_quantity"]);

        if (Price is { } price && decimal.Round(price, 4) != price)
            yield return new ValidationResult("Price supports up to four decimal places.", ["price"]);
    }
}
