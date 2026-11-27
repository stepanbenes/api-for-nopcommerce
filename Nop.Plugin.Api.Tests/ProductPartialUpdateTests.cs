using Moq;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Discounts;
using Nop.Core.Domain.Stores;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Plugin.Api.Controllers;
using Nop.Plugin.Api.Delta;
using Nop.Plugin.Api.DTO.Products;
using Nop.Plugin.Api.Helpers;
using Nop.Plugin.Api.JSON.Serializers;
using Nop.Plugin.Api.Maps;
using Nop.Plugin.Api.Services;
using Nop.Services.Authentication;
using Nop.Services.Catalog;
using Nop.Services.Discounts;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Security;
using Nop.Services.Stores;
using NUnit.Framework;

namespace Nop.Plugin.Api.Tests;

[TestFixture]
[NonParallelizable]
public class ProductPartialUpdateTests
{
    private static readonly DateTime PreviousUpdate = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private Product _product;
    private Mock<IProductService> _products;
    private Mock<IProductTagService> _tags;
    private Mock<IDiscountService> _discounts;
    private ProductsController _controller;
    private IAuthenticationService _authentication;
    private IStoreContext _store;
    private ProductSyncScope _scope;

    [SetUp]
    public void SetUp()
    {
        _product = new Product { Id = 1, Published = true, Price = 15m, UpdatedOnUtc = PreviousUpdate };
        _products = new Mock<IProductService>();
        _tags = new Mock<IProductTagService>();
        _discounts = new Mock<IDiscountService>();
        var actor = new Customer { Id = 11 };
        var authentication = new Mock<IAuthenticationService>();
        authentication.Setup(service => service.GetAuthenticatedCustomerAsync()).ReturnsAsync(actor);
        _authentication = authentication.Object;
        var store = new Mock<IStoreContext>();
        store.Setup(service => service.GetCurrentStoreAsync()).ReturnsAsync(new Store { Id = 12 });
        _store = store.Object;
        var repository = new Mock<IRepository<Product>>();
        repository.SetupGet(service => service.Table).Returns(new[] { _product }.AsQueryable());
        var stores = new Mock<IStoreMappingService>();
        stores.Setup(service => service.ApplyStoreMapping(It.IsAny<IQueryable<Product>>(), 12))
            .ReturnsAsync((IQueryable<Product> query, int _) => query);
        var acl = new Mock<IAclService>();
        acl.Setup(service => service.ApplyAcl(It.IsAny<IQueryable<Product>>(), actor))
            .ReturnsAsync((IQueryable<Product> query, Customer _) => query);
        _scope = new ProductSyncScope(repository.Object, stores.Object, acl.Object);

        _controller = new ProductsController(Mock.Of<IProductApiService>(), Mock.Of<IJsonFieldsSerializer>(), _products.Object,
            null, Mock.Of<ICustomerActivityService>(), Mock.Of<ILocalizationService>(), null, null, null, null,
            null, _discounts.Object, null, null, _tags.Object, null, Mock.Of<IDTOHelper>(), null);
    }

    [TestCase(15, false)]
    [TestCase(20, true)]
    public async Task PriceOnlyPreservesPublicationAndRelationships(int price, bool changed)
    {
        await UpdateProduct(CreateDelta(("price", price)));

        Assert.That(_product.Published, Is.True);
        Assert.That(_product.Price, Is.EqualTo(price));
        Assert.That(_product.UpdatedOnUtc > PreviousUpdate, Is.EqualTo(changed));
        _products.Verify(service => service.UpdateProductAsync(_product), changed ? Times.Once() : Times.Never());
        _products.Verify(service => service.GetProductPicturesByProductIdAsync(_product.Id), Times.Never());
        _products.Verify(service => service.GetAllDiscountsAppliedToProductAsync(_product.Id), Times.Never());
        _tags.VerifyNoOtherCalls();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExplicitTagCollectionCanClearOrReplaceExistingTags(bool replace)
    {
        _tags.Setup(service => service.GetAllProductTagsByProductIdAsync(_product.Id))
            .ReturnsAsync(new List<ProductTag> { new() { Name = "existing" } });
        var requested = replace ? new List<object> { "replacement" } : new List<object>();

        await UpdateProduct(CreateDelta(("tags", requested)));

        _tags.Verify(service => service.UpdateProductTagsAsync(_product,
            It.Is<string[]>(tags => tags.SequenceEqual(requested.Cast<string>()))), Times.Once());
        _products.Verify(service => service.UpdateProductAsync(_product), Times.Once());
    }

    [Test]
    public async Task EqualTagsAndPictureOrderSkipWrites()
    {
        _tags.Setup(service => service.GetAllProductTagsByProductIdAsync(_product.Id))
            .ReturnsAsync(new List<ProductTag> { new() { Name = "Existing" } });
        _products.Setup(service => service.GetProductPicturesByProductIdAsync(_product.Id))
            .ReturnsAsync(new List<ProductPicture> { new() { Id = 7, DisplayOrder = 9 } });

        await UpdateProduct(CreateDelta(("tags", new List<object> { "existing" }),
            ("images", new List<object> { new Dictionary<string, object> { ["id"] = 7, ["position"] = 9 } })));

        _tags.Verify(service => service.UpdateProductTagsAsync(_product, It.IsAny<string[]>()), Times.Never());
        _products.Verify(service => service.UpdateProductPictureAsync(It.IsAny<ProductPicture>()), Times.Never());
        _products.Verify(service => service.UpdateProductAsync(_product), Times.Never());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExplicitDiscountCollectionPersistsOnlyChangedMappings(bool replace)
    {
        var mapping = new DiscountProductMapping { EntityId = _product.Id, DiscountId = 1 };
        _products.Setup(service => service.GetAllDiscountsAppliedToProductAsync(_product.Id))
            .ReturnsAsync(new List<DiscountProductMapping> { mapping });
        _discounts.Setup(service => service.GetDiscountByIdAsync(2)).ReturnsAsync(
            new Discount { Id = 2, DiscountType = DiscountType.AssignedToSkus });
        var requested = replace ? new List<object> { 2 } : new List<object>();

        await UpdateProduct(CreateDelta(("discount_ids", requested)));

        _products.Verify(service => service.DeleteDiscountProductMappingAsync(mapping), Times.Once());
        _products.Verify(service => service.InsertDiscountProductMappingAsync(
            It.Is<DiscountProductMapping>(entry => entry.EntityId == _product.Id && entry.DiscountId == 2)),
            replace ? Times.Once() : Times.Never());
    }

    private Task UpdateProduct(Delta<ProductDto> delta) =>
        _controller.UpdateProduct(delta, _authentication, _store, _scope);

    private Delta<ProductDto> CreateDelta(params (string Key, object Value)[] changes)
    {
        var values = changes.ToDictionary(entry => entry.Key, entry => entry.Value);
        values["id"] = _product.Id;
        var engine = new Mock<IEngine>();
        engine.Setup(instance => instance.Resolve<IJsonPropertyMapper>(null)).Returns(new JsonPropertyMapper());
        var previous = EngineContext.Current;
        EngineContext.Replace(engine.Object);
        try
        {
            return new Delta<ProductDto>(values);
        }
        finally
        {
            EngineContext.Replace(previous);
        }
    }
}
