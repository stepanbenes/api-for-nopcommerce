using Moq;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Stores;
using Nop.Core.Domain.Vendors;
using Nop.Data;
using Nop.Plugin.Api.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Stores;
using NUnit.Framework;

namespace Nop.Plugin.Api.Tests.ServicesTests;

public class ScopedApiQueriesTests
{
    private static IStoreContext CurrentStore()
    {
        var context = new Mock<IStoreContext>();
        context.Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Store { Id = 1 });
        return context.Object;
    }

    private static IRepository<T> Repository<T>(params T[] rows) where T : BaseEntity
    {
        var repository = new Mock<IRepository<T>>();
        repository.Setup(x => x.Table).Returns(rows.AsQueryable());
        return repository.Object;
    }

    [Test]
    public async Task ProductVendorAndStoreFiltersApplyBeforePagingAndCounting()
    {
        var products = Repository(
            new Product { Id = 1, VendorId = 1, Published = true, LimitedToStores = true },
            new Product { Id = 2, VendorId = 1, Published = true, Deleted = true },
            new Product { Id = 3, VendorId = 1 },
            new Product { Id = 4, VendorId = 1, Published = true },
            new Product { Id = 5, VendorId = 2, Published = true });
        var mapping = new Mock<IStoreMappingService>(MockBehavior.Strict);
        mapping.Setup(x => x.ApplyStoreMapping(It.IsAny<IQueryable<Product>>(), 1))
            .ReturnsAsync((IQueryable<Product> query, int _) => query.Where(row => !row.LimitedToStores));
        var service = new ProductApiService(products, Repository<ProductCategory>(),
            Repository(new Vendor { Id = 1, Name = "vendor", Active = true }), mapping.Object, CurrentStore());

        var page = await service.GetProductsAsync(vendorName: "vendor", publishedStatus: true, limit: 1);
        var count = await service.GetProductsCountAsync(vendorName: "vendor", publishedStatus: true);

        Assert.That(page.Select(product => product.Id), Is.EqualTo(new[] { 4 }));
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task CategoryStoreFilterAppliesBeforePagingAndCounting()
    {
        var categories = Repository(new Category { Id = 1, LimitedToStores = true },
            new Category { Id = 2 }, new Category { Id = 3 });
        var mapping = new Mock<IStoreMappingService>(MockBehavior.Strict);
        mapping.Setup(x => x.ApplyStoreMapping(It.IsAny<IQueryable<Category>>(), 1))
            .ReturnsAsync((IQueryable<Category> query, int _) => query.Where(row => !row.LimitedToStores));
        var service = new CategoryApiService(categories, Repository<ProductCategory>(),
            mapping.Object, CurrentStore(), Mock.Of<ICategoryService>());

        var page = await service.GetCategoriesAsync(limit: 1, page: 2);
        var count = await service.GetCategoriesCountAsync();

        Assert.That(page.Select(category => category.Id), Is.EqualTo(new[] { 3 }));
        Assert.That(count, Is.EqualTo(2));
    }

    [Test]
    public async Task CustomerSearchScopesAndFiltersBeforePaging()
    {
        var customers = Repository(
            new Customer { Id = 1, RegisteredInStoreId = 2, Email = "user1" },
            new Customer { Id = 2, RegisteredInStoreId = 1, Email = "user2" },
            new Customer { Id = 3, Email = "user3" },
            new Customer { Id = 4, Email = "other" },
            new Customer { Id = 5, Email = "user5" });
        var attributes = Repository(
            new GenericAttribute { EntityId = 1, KeyGroup = "Customer", Key = NopCustomerDefaults.EuCookieLawAcceptedAttribute, Value = "true" },
            new GenericAttribute { EntityId = 3, KeyGroup = "Customer", Key = NopCustomerDefaults.EuCookieLawAcceptedAttribute, Value = "true" },
            new GenericAttribute { EntityId = 4, KeyGroup = "Customer", Key = NopCustomerDefaults.EuCookieLawAcceptedAttribute, Value = "true" },
            new GenericAttribute { EntityId = 5, KeyGroup = "Customer", Key = NopCustomerDefaults.EuCookieLawAcceptedAttribute, Value = "true" });
        var customerService = new Mock<ICustomerService>();
        customerService.Setup(x => x.GetCustomerRolesAsync(It.IsAny<Customer>(), true))
            .ReturnsAsync(new List<CustomerRole>());
        var service = new CustomerApiService(customers, attributes, Repository<NewsLetterSubscription>(),
            CurrentStore(), Mock.Of<ILanguageService>(), Mock.Of<IStaticCacheManager>(),
            Mock.Of<IAddressApiService>(), Mock.Of<ICurrencyService>(), customerService.Object);

        var page = await service.SearchAsync("email:user EuCookieLaw.Accepted:true", page: 2, limit: 1);

        Assert.That(page.Select(customer => customer.Id), Is.EqualTo(new[] { 5 }));
        Assert.That(page.Single().EuCookieLawAccepted, Is.True);

        var firstDescending = await service.SearchAsync("email:user EuCookieLaw.Accepted:true", order: "Id DESC", page: 1, limit: 1);
        var nextDescending = await service.SearchAsync("email:user EuCookieLaw.Accepted:true", order: "Id DESC", page: 2, limit: 1);
        var tied = await service.SearchAsync("email:user EuCookieLaw.Accepted:true", order: "eu_cookie_law_accepted desc", page: 1, limit: 1);
        Assert.That(firstDescending.Single().Id, Is.EqualTo(5));
        Assert.That(nextDescending.Single().Id, Is.EqualTo(3));
        Assert.That(tied.Single().Id, Is.EqualTo(3));
    }

    [Test]
    public async Task CustomerLookupHidesDeletedRowsWithOrWithoutGenericAttributes()
    {
        var customers = Repository(new Customer { Id = 1, Deleted = true }, new Customer { Id = 2, Deleted = true });
        var attributes = Repository(new GenericAttribute
        {
            EntityId = 1, KeyGroup = "Customer", Key = NopCustomerDefaults.EuCookieLawAcceptedAttribute, Value = "true"
        });
        var customerService = new Mock<ICustomerService>();
        customerService.Setup(service => service.GetCustomerRolesAsync(It.IsAny<Customer>(), true))
            .ReturnsAsync(new List<CustomerRole>());
        var service = new CustomerApiService(customers, attributes, Repository<NewsLetterSubscription>(),
            CurrentStore(), Mock.Of<ILanguageService>(), Mock.Of<IStaticCacheManager>(),
            Mock.Of<IAddressApiService>(), Mock.Of<ICurrencyService>(), customerService.Object);

        Assert.That(await service.GetCustomerByIdAsync(1), Is.Null);
        Assert.That(await service.GetCustomerByIdAsync(2), Is.Null);
        Assert.That((await service.GetCustomerByIdAsync(1, showDeleted: true)).EuCookieLawAccepted, Is.True);
        Assert.That((await service.GetCustomerByIdAsync(2, showDeleted: true)).Id, Is.EqualTo(2));
    }

    [Test]
    public async Task ManufacturerLocalizationUsesRequestedLanguageAndNameKey()
    {
        var properties = Repository(
            new LocalizedProperty { EntityId = 2, LocaleKeyGroup = "Manufacturer", LocaleKey = "Name", LanguageId = 1, LocaleValue = "wrong language" },
            new LocalizedProperty { EntityId = 2, LocaleKeyGroup = "Manufacturer", LocaleKey = "Description", LanguageId = 2, LocaleValue = "wrong key" },
            new LocalizedProperty { EntityId = 2, LocaleKeyGroup = "Manufacturer", LocaleKey = "Name", LanguageId = 2, LocaleValue = "translated" });
        var service = new ManufacturerApiService(
            Repository(new Manufacturer { Id = 1 }, new Manufacturer { Id = 2, Name = "original" }),
            Repository<ProductManufacturer>(), properties);

        var page = await service.GetManufacturersAsync(languageId: 2, page: 2, limit: 1);

        Assert.That(page.Single().Name, Is.EqualTo("translated"));
    }
}
