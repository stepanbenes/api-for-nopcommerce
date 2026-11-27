#nullable enable

using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using Nop.Core.Domain.Menus;

namespace Nop.Plugin.Api.DTOs.Menus;

public record MenuDto(
    [property: JsonProperty("id")] int Id,
    [property: JsonProperty("name")] string Name,
    [property: JsonProperty("menu_type")] MenuType MenuType,
    [property: JsonProperty("published")] bool Published,
    [property: JsonProperty("display_order")] int DisplayOrder,
    [property: JsonProperty("display_all_categories")] bool DisplayAllCategories);

public record MenuItemDto(
    [property: JsonProperty("id")] int Id,
    [property: JsonProperty("menu_id")] int MenuId,
    [property: JsonProperty("parent_id")] int ParentId,
    [property: JsonProperty("item_type")] MenuItemType ItemType,
    [property: JsonProperty("entity_id")] int? EntityId,
    [property: JsonProperty("title")] string? Title,
    [property: JsonProperty("url")] string? Url,
    [property: JsonProperty("published")] bool Published,
    [property: JsonProperty("display_order")] int DisplayOrder);

public record MenusResponse([property: JsonProperty("menus")] IReadOnlyList<MenuDto> Menus);
public record MenuItemsResponse([property: JsonProperty("menu_items")] IReadOnlyList<MenuItemDto> Items);

public class AddMenuItemRequest
{
    [Required]
    [RegularExpression("^(category|topic)$")]
    [JsonProperty("link_type")]
    public string LinkType { get; set; } = null!;

    [Range(1, int.MaxValue)]
    [JsonProperty("entity_id")]
    public int EntityId { get; set; }

    [JsonProperty("display_order")]
    public int? DisplayOrder { get; set; }
}

public class UpdateMenuItemOrderRequest
{
    [Required]
    [JsonProperty("expected_display_order")]
    public int? ExpectedDisplayOrder { get; set; }

    [Required]
    [JsonProperty("display_order")]
    public int? DisplayOrder { get; set; }
}
