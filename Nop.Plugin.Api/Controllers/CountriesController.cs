using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Api.Attributes;
using Nop.Plugin.Api.Authorization.Attributes;
using Nop.Plugin.Api.DTO;
using Nop.Plugin.Api.DTO.Errors;
using Nop.Plugin.Api.JSON.ActionResults;
using Nop.Plugin.Api.JSON.Serializers;
using Nop.Plugin.Api.Services;
using Nop.Services.Customers;
using Nop.Services.Discounts;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Media;
using Nop.Services.Security;
using Nop.Services.Stores;

namespace Nop.Plugin.Api.Controllers
{
	public class CountriesController : BaseApiController
	{
		private readonly IAddressApiService addressApiService;

		public CountriesController(
			IJsonFieldsSerializer jsonFieldsSerializer,
			IAclService aclService,
			ICustomerService customerService,
			IStoreMappingService storeMappingService,
			IStoreService storeService,
			IDiscountService discountService,
			ICustomerActivityService customerActivityService,
			ILocalizationService localizationService,
			IPictureService pictureService,
			IAddressApiService addressApiService)
			: base(jsonFieldsSerializer, aclService, customerService, storeMappingService, storeService, discountService, customerActivityService, localizationService, pictureService)
		{
			this.addressApiService = addressApiService;
		}

        /// <summary>
        ///     Receive a list of all Countries
        /// </summary>
        /// <response code="200">OK</response>
        /// <response code="400">Bad Request</response>
        /// <response code="401">Unauthorized</response>
        [HttpGet]
        [Route("/api/countries", Name = "GetCountries")]
        [ProducesResponseType(typeof(CountriesRootObject), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ErrorsRootObject), (int)HttpStatusCode.BadRequest)]
        [GetRequestsErrorInterceptorActionFilter]
        public async Task<IActionResult> GetCountries([FromQuery] bool? mustAllowBilling = null, [FromQuery] bool? mustAllowShipping = null)
        {
            var countriesDtos = await addressApiService.GetAllCountriesAsync(mustAllowBilling ?? false, mustAllowShipping ?? false);

            var countriesRootObject = new CountriesRootObject
            {
                Countries = countriesDtos
            };

            var json = JsonFieldsSerializer.Serialize(countriesRootObject, string.Empty);

            return new RawJsonActionResult(json);
        }
    }
}
