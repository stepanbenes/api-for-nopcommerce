using Newtonsoft.Json;
using Nop.Plugin.Api.DTO.Base;

namespace Nop.Plugin.Api.DTO.Orders
{
    public class CheckoutAttributesRootObject
    {
        [JsonProperty("checkout_attributes")]
        public List<CheckoutAttributeDto> CheckoutAttributes { get; set; } = new();
    }

    public class CheckoutAttributeDto : BaseDto
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("required")]
        public bool Required { get; set; }

        [JsonProperty("control_type")]
        public string ControlType { get; set; }

        [JsonProperty("condition", NullValueHandling = NullValueHandling.Ignore)]
        public CheckoutAttributeConditionDto Condition { get; set; }

        [JsonProperty("choices")]
        public List<CheckoutAttributeChoiceDto> Choices { get; set; } = new();
    }

    public class CheckoutAttributeConditionDto
    {
        [JsonProperty("attribute_id")]
        public int AttributeId { get; set; }

        [JsonProperty("values")]
        public IList<string> Values { get; set; }
    }

    public class CheckoutAttributeChoiceDto : BaseDto
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("preselected")]
        public bool Preselected { get; set; }
    }
}
