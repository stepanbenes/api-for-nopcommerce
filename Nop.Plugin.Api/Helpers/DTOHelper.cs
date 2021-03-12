using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Stores;
using Nop.Core.Domain.Topics;
using Nop.Plugin.Api.DTO.Categories;
using Nop.Plugin.Api.DTO.Images;
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
using Nop.Plugin.Api.MappingExtensions;
using Nop.Plugin.Api.Services;
//using Nop.Plugin.Api.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Discounts;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Services.Seo;
using Nop.Services.Stores;
using Nop.Services.Topics;

namespace Nop.Plugin.Api.Helpers
{
    public class DTOHelper : IDTOHelper
    {
        private readonly IAclService _aclService;
        private readonly ICurrencyService _currencyService;
        private readonly CurrencySettings _currencySettings;
        private readonly ICustomerService _customerService;
        private readonly ILanguageService _languageService;
        private readonly ILocalizationService _localizationService;
        private readonly IPictureService _pictureService;
        private readonly IProductAttributeParser _productAttributeParser;
        private readonly IProductAttributeService _productAttributeService;
        private readonly IProductService _productService;
        private readonly IProductTagService _productTagService;
        private readonly IDiscountService _discountService;
        private readonly IManufacturerService _manufacturerService;
        private readonly IOrderService _orderService;
        private readonly IProductAttributeConverter _productAttributeConverter;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly IProductService _productPictureService;
        private readonly IStoreMappingService _storeMappingService;
        private readonly IStoreService _storeService;
        private readonly IUrlRecordService _urlRecordService;

        public DTOHelper(
            IProductService productService,
            IAclService aclService,
            IStoreMappingService storeMappingService,
            IPictureService pictureService,
            IProductAttributeService productAttributeService,
            ICustomerService customerApiService,
            IProductAttributeParser productAttributeParser,
            ILanguageService languageService,
            ICurrencyService currencyService,
            CurrencySettings currencySettings,
            IStoreService storeService,
            ILocalizationService localizationService,
            IUrlRecordService urlRecordService,
            IProductTagService productTagService,
            IDiscountService discountService,
            IManufacturerService manufacturerService,
            IOrderService orderService,
            IProductAttributeConverter productAttributeConverter,
            IShoppingCartService shoppingCartService)
        {
            _productService = productService;
            _aclService = aclService;
            _storeMappingService = storeMappingService;
            _pictureService = pictureService;
            _productAttributeService = productAttributeService;
            _customerService = customerApiService;
            _productAttributeParser = productAttributeParser;
            _languageService = languageService;
            _currencyService = currencyService;
            _currencySettings = currencySettings;
            _storeService = storeService;
            _localizationService = localizationService;
            _urlRecordService = urlRecordService;
            _productTagService = productTagService;
            _discountService = discountService;
            _manufacturerService = manufacturerService;
            _orderService = orderService;
            _productAttributeConverter = productAttributeConverter;
            _shoppingCartService = shoppingCartService;
        }

        public async Task<CategoryDto> PrepareCategoryDTOAsync(Category category)
        {
            var categoryDto = category.ToDto();

            var picture = await _pictureService.GetPictureByIdAsync(category.PictureId);
            var imageDto = PrepareImageDto(picture);

            if (imageDto != null)
            {
                categoryDto.Image = imageDto;
            }

            categoryDto.SeName = await _urlRecordService.GetSeNameAsync(category);
            categoryDto.DiscountIds = (await _discountService.GetAppliedDiscountsAsync(category)).Select(discount => discount.Id).ToList();
            categoryDto.RoleIds = (await _aclService.GetAclRecordsAsync(category)).Select(acl => acl.CustomerRoleId).ToList();
            categoryDto.StoreIds = (await _storeMappingService.GetStoreMappingsAsync(category)).Select(mapping => mapping.StoreId)
                                                       .ToList();

            var allLanguages = await _languageService.GetAllLanguagesAsync();

            categoryDto.LocalizedNames = new List<LocalizedNameDto>();

            foreach (var language in allLanguages)
            {
                var localizedNameDto = new LocalizedNameDto
                {
                    LanguageId = language.Id,
                    LocalizedName = await _localizationService.GetLocalizedAsync(category, x => x.Name, language.Id)
                };

                categoryDto.LocalizedNames.Add(localizedNameDto);
            }

            return categoryDto;
        }

        protected ImageDto PrepareImageDto(Picture picture)
        {
            ImageDto image = null;

            if (picture != null)
            {
                // We don't use the image from the passed dto directly 
                // because the picture may be passed with src and the result should only include the base64 format.
                image = new ImageDto
                {
                    //Attachment = Convert.ToBase64String(picture.PictureBinary),
                    //Src = _pictureService.GetPictureUrl(picture)
                };
            }

            return image;
        }
    }
}
