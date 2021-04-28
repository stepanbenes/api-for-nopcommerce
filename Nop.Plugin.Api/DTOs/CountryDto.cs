using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Nop.Plugin.Api.DTO
{
	public class CountryDto
	{
		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("allows_billing")]
		public bool AllowsBilling { get; set; }

		[JsonProperty("allows_shipping")]
		public bool AllowsShipping { get; set; }

		[JsonProperty("two_letter_iso_code")]
		public string TwoLetterIsoCode { get; set; }

		[JsonProperty("ThreeLetterIsoCode")]
		public string ThreeLetterIsoCode { get; set; }

		[JsonProperty("numeric_iso_code")]
		public int NumericIsoCode { get; set; }

		[JsonProperty("subject_to_vat")]
		public bool SubjectToVat { get; set; }
	}
}
