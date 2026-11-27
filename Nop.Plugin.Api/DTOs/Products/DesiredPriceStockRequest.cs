using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Nop.Plugin.Api.DTO.Products;

public sealed class DesiredPriceStockRequest : IValidatableObject
{
    [JsonProperty("items", Required = Required.Always)]
    [MinLength(1), MaxLength(100)]
    public List<DesiredPriceStockItem> Items { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Items.Contains(null))
            yield return new ValidationResult("Items cannot contain null.", ["items"]);
        else if (Items.Select(item => item.ProductId).Distinct().Count() != Items.Count)
            yield return new ValidationResult("Each product ID must appear only once.", ["items"]);
    }
}

public sealed class DesiredPriceStockItem : IValidatableObject
{
    [JsonProperty("product_id", Required = Required.Always)]
    [Range(1, int.MaxValue)]
    public int ProductId { get; init; }

    [JsonProperty("expected_price")]
    [Range(typeof(decimal), "0", "99999999999999.9999", ParseLimitsInInvariantCulture = true)]
    public decimal? ExpectedPrice { get; init; }

    [JsonProperty("price")]
    [Range(typeof(decimal), "0", "99999999999999.9999", ParseLimitsInInvariantCulture = true)]
    public decimal? Price { get; init; }

    [JsonProperty("expected_stock_quantity")]
    public int? ExpectedStockQuantity { get; init; }

    [JsonProperty("stock_quantity")]
    [Range(0, int.MaxValue)]
    public int? StockQuantity { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Price.HasValue && !StockQuantity.HasValue)
            yield return new ValidationResult("Supply at least one of price or stock_quantity.", ["price", "stock_quantity"]);

        if (Price.HasValue != ExpectedPrice.HasValue)
            yield return new ValidationResult("Supply price and expected_price together.", ["price", "expected_price"]);

        if (StockQuantity.HasValue != ExpectedStockQuantity.HasValue)
            yield return new ValidationResult("Supply stock_quantity and expected_stock_quantity together.",
                ["stock_quantity", "expected_stock_quantity"]);

        if (Price is { } price && decimal.Round(price, 4) != price ||
            ExpectedPrice is { } expected && decimal.Round(expected, 4) != expected)
            yield return new ValidationResult("Price supports up to four decimal places.", ["price", "expected_price"]);
    }
}
