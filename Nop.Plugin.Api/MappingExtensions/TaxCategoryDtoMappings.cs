using Nop.Core.Domain.Tax;
using Nop.Plugin.Api.AutoMapper;
using Nop.Plugin.Api.DTOs.Taxes;

namespace Nop.Plugin.Api.MappingExtensions
{
    public static class TaxCategoryDtoMappings
    {
        public static TaxCategoryDto ToDto(this TaxCategory taxCategory)
        {
            return taxCategory.MapTo<TaxCategory, TaxCategoryDto>();
        }
    }
}
