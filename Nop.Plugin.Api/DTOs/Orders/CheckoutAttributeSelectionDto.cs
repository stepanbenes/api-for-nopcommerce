using Newtonsoft.Json;

namespace Nop.Plugin.Api.DTO.Orders
{
    public class CheckoutAttributeSelectionDto
    {
        [JsonProperty("id")]
        public int AttributeId { get; set; }

        [JsonProperty("values")]
        public List<string> Values { get; set; }
    }
}
