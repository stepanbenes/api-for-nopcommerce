using Nop.Core.Domain.Directory;
using Nop.Plugin.Api.AutoMapper;
using Nop.Plugin.Api.DTO;

namespace Nop.Plugin.Api.MappingExtensions
{
    public static class CurrencyDtoMappings
    {
        public static CurrencyDto ToDto(this Currency currency)
        {
            return currency.MapTo<Currency, CurrencyDto>();
        }
    }
}
