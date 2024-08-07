using Newtonsoft.Json;
using Nop.Plugin.Api.DTO;

namespace Nop.Plugin.Api.DTOs.StateProvinces
{
    public class StateProvincesRootObject : ISerializableObject
    {
        public StateProvincesRootObject()
        {
            StateProvinces = new List<StateProvinceDto>();
        }

        [JsonProperty("state_provinces")]
        public IList<StateProvinceDto> StateProvinces { get; set; }

        public string GetPrimaryPropertyName()
        {
            return "state_provinces";
        }

        public Type GetPrimaryPropertyType()
        {
            return typeof(StateProvinceDto);
        }
    }
}
