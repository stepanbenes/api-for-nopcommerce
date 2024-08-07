﻿using Newtonsoft.Json;
using Nop.Plugin.Api.DTO.Base;

namespace Nop.Plugin.Api.DTOs.StateProvinces
{

    [JsonObject(Title = "province")]
    public class StateProvinceDto : BaseDto
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("abbreviation")]
        public string Abbreviation { get; set; }

        [JsonProperty("published")]
        public bool Published { get; set; }

        [JsonProperty("display_order")]
        public int DisplayOrder { get; set; }
    }

}
