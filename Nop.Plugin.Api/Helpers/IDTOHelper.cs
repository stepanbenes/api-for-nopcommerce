using System.Threading.Tasks;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Stores;
using Nop.Core.Domain.Topics;
using Nop.Plugin.Api.DTO.Categories;
using Nop.Plugin.Api.DTO.Languages;
using Nop.Plugin.Api.DTO.Manufacturers;
using Nop.Plugin.Api.DTO.OrderItems;
using Nop.Plugin.Api.DTO.Orders;
using Nop.Plugin.Api.DTO.ProductAttributes;
using Nop.Plugin.Api.DTO.Products;
using Nop.Plugin.Api.DTO.ShoppingCarts;
using Nop.Plugin.Api.DTO.SpecificationAttributes;
using Nop.Plugin.Api.DTO.Stores;
using Nop.Plugin.Api.DTOs.Topics;

namespace Nop.Plugin.Api.Helpers
{
    public interface IDTOHelper
    {
        Task<CategoryDto> PrepareCategoryDTOAsync(Category category);
    }
}
