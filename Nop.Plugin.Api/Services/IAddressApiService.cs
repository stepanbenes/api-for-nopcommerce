using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nop.Plugin.Api.DTO;

namespace Nop.Plugin.Api.Services
{
	public interface IAddressApiService
	{
		Task<IList<AddressDto>> GetAddressesByCustomerIdAsync(int customerId);
		Task<AddressDto> GetCustomerAddressAsync(int customerId, int addressId);
		Task<IList<CountryDto>> GetAllCountriesAsync(bool mustAllowBilling = false, bool mustAllowShipping = false);
		Task<AddressDto> GetAddressByIdAsync(int addressId);
	}
}
