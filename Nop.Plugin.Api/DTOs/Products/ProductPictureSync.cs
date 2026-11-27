using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Nop.Plugin.Api.DTO.Products;

public sealed class ProductPictureSyncRequest
{
    [JsonProperty("expected_sha256")]
    [RegularExpression("^[0-9a-f]{64}$")]
    public string ExpectedSha256 { get; set; }

    [JsonProperty("sha256")]
    [Required, RegularExpression("^[0-9a-f]{64}$")]
    public string Sha256 { get; set; }

    [JsonProperty("mime_type")]
    public string MimeType { get; set; }

    [JsonProperty("base64")]
    public string Base64 { get; set; }
}

public sealed record ProductPictureSyncResult(
    [property: JsonProperty("status")] string Status,
    [property: JsonProperty("mapping_id")] int? MappingId = null,
    [property: JsonProperty("picture_id")] int? PictureId = null,
    [property: JsonProperty("sha256")] string Sha256 = null);
