using Mapster;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Stores;
using Nop.Core.Domain.Tax;
using Nop.Core.Domain.Topics;
using Nop.Core.Infrastructure.Mapper;
using Nop.Plugin.Api.Areas.Admin.Models;
using Nop.Plugin.Api.Domain;
using Nop.Plugin.Api.DTO;
using Nop.Plugin.Api.DTO.Categories;
using Nop.Plugin.Api.DTO.CustomerRoles;
using Nop.Plugin.Api.DTO.Customers;
using Nop.Plugin.Api.DTO.Languages;
using Nop.Plugin.Api.DTO.Manufacturers;
using Nop.Plugin.Api.DTO.NewsLetterSubscriptions;
using Nop.Plugin.Api.DTO.OrderItems;
using Nop.Plugin.Api.DTO.Orders;
using Nop.Plugin.Api.DTO.ProductAttributes;
using Nop.Plugin.Api.DTO.ProductCategoryMappings;
using Nop.Plugin.Api.DTO.ProductManufacturerMappings;
using Nop.Plugin.Api.DTO.Products;
using Nop.Plugin.Api.DTO.ProductWarehouseIventories;
using Nop.Plugin.Api.DTO.ShoppingCarts;
using Nop.Plugin.Api.DTO.SpecificationAttributes;
using Nop.Plugin.Api.DTO.Stores;
using Nop.Plugin.Api.DTO.Warehouses;
using Nop.Plugin.Api.DTOs.StateProvinces;
using Nop.Plugin.Api.DTOs.Taxes;
using Nop.Plugin.Api.DTOs.Topics;


namespace Nop.Plugin.Api.MappingExtensions
{
    public class ApiMapperConfiguration : BaseMapperProfile
    {
        public override void Init()
        {
            CreateMap<ApiSettings, ConfigurationModel>();
            CreateMap<ConfigurationModel, ApiSettings>();

            CreateMap<Category, CategoryDto>();
            CreateMap<CategoryDto, Category>();

            CreateMap<Store, StoreDto>();

            CreateMap<ProductCategory, ProductCategoryMappingDto>();

            CreateMap<ProductManufacturer, ProductManufacturerMappingsDto>();
            CreateMap<ProductWarehouseInventory, ProductWarehouseInventoryDto>()
                .ForMember(dto => dto.ExpectedStockQuantity, map => map.Ignore());

            CreateMap<Warehouse, WarehouseDto>();
            CreateMap<WarehouseDto, Warehouse>();

            CreateMap<Language, LanguageDto>();

            CreateMap<CustomerRole, CustomerRoleDto>();

            CreateMap<Manufacturer, ManufacturerDto>();

            CreateMap<Address, AddressDto>();
            CreateMap<AddressDto, Address>();
            CreateMap<ShoppingCartItem, ShoppingCartItemDto>();
            CreateMap<Customer, CustomerDto>();

            CreateMap<OrderItem, OrderItemDto>();
            CreateMap<Order, OrderDto>();

            CreateMap<Product, ProductDto>().Ignore(product => product.RequiredProductIds);

            CreateMap<ProductAttributeValue, ProductAttributeValueDto>();

            CreateMap<ProductAttribute, ProductAttributeDto>();

            CreateMap<ProductSpecificationAttribute, ProductSpecificationAttributeDto>();
            CreateMap<ProductAttributeCombination, ProductAttributeCombinationDto>();

            CreateMap<SpecificationAttribute, SpecificationAttributeDto>();
            CreateMap<SpecificationAttributeOption, SpecificationAttributeOptionDto>();

            CreateMap<NewsLetterSubscriptionDto, NewsLetterSubscription>();
            CreateMap<NewsLetterSubscription, NewsLetterSubscriptionDto>();

            CreateMap<Topic, TopicDto>();

            CreateMap<StateProvince, StateProvinceDto>();
            CreateMap<StateProvinceDto, StateProvince>();

            CreateMap<Country, CountryDto>();
            CreateMap<CountryDto, Country>();

            CreateMap<Currency, CurrencyDto>();

            CreateMap<TaxCategory, TaxCategoryDto>();
        }

        private new TypeAdapterSetter<TSource, TDestination> CreateMap<TSource, TDestination>()
        {
            var mapping = base.CreateMap<TSource, TDestination>();
            var sourceType = typeof(TSource);

            foreach (var property in typeof(TDestination).GetProperties())
            {
                if (sourceType.GetProperty(property.Name) == null)
                {
                    mapping.Ignore(property.Name);
                }
            }

            return mapping;
        }
    }
}
