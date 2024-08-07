using Newtonsoft.Json;
using Nop.Plugin.Api.DTO;

namespace Nop.Plugin.Api.DTOs.Taxes
{
    public class TaxCategoriesRootObject : ISerializableObject
    {
        public TaxCategoriesRootObject()
        {
            Taxes = new List<TaxCategoryDto>();
        }

        [JsonProperty("tax_categories")]
        public IList<TaxCategoryDto> Taxes { get; set; }

        public string GetPrimaryPropertyName()
        {
            return "tax_categories";
        }

        public Type GetPrimaryPropertyType()
        {
            return typeof(TaxCategoryDto);
        }

    }

}
