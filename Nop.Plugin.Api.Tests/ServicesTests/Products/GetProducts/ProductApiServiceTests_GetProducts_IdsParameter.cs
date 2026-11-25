using System.Collections.Generic;
using System.Linq;
using Nop.Data;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Vendors;
using Nop.Plugin.Api.Services;
using NUnit.Framework;
using Moq;

namespace Nop.Plugin.Api.Tests.ServicesTests.Products.GetProducts
{
    using Nop.Services.Stores;

    [TestFixture]
    public class ProductApiServiceTests_GetProducts_IdsParameter
    {
        private IProductApiService _productApiService;
        private List<Product> _existigProducts;

        [SetUp]
        public void Setup()
        {
            _existigProducts = new List<Product>()
            {
                new Product() {Id = 2},
                new Product() {Id = 3},
                new Product() {Id = 1},
                new Product() {Id = 4},
                new Product() {Id = 5},
                new Product() {Id = 6, Deleted = true},
                new Product() {Id = 7, Published = false}
            };

            var productRepo = Mock.Of<IRepository<Product>>();
            Mock.Get(productRepo).Setup(x => x.Table).Returns(_existigProducts.AsQueryable());

            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();
            var vendorRepo = Mock.Of<IRepository<Vendor>>();

            var storeMappingService = Mock.Of<IStoreMappingService>();
            Mock.Get(storeMappingService).Setup(x => x.ApplyStoreMapping(It.IsAny<IQueryable<Product>>(), 1))
                .ReturnsAsync((IQueryable<Product> query, int _) => query);
            var storeContext = Mock.Of<Nop.Core.IStoreContext>();
            Mock.Get(storeContext).Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Nop.Core.Domain.Stores.Store { Id = 1 });
            
            _productApiService = new ProductApiService(productRepo, productCategoryRepo, vendorRepo, storeMappingService, storeContext);
        }
        
        [Test]
        public async Task WhenCalledWithIdsParameter_GivenProductsWithTheSpecifiedIds_ShouldReturnThemSortedById()
        {
            var idsCollection = new List<int>() { 1, 5 };

            var products = await _productApiService.GetProductsAsync(ids: idsCollection);

            // Assert
            CollectionAssert.IsNotEmpty(products);
            Assert.AreEqual(idsCollection[0], products[0].Id);
            Assert.AreEqual(idsCollection[1], products[1].Id);
        }

        [Test]
        public async Task WhenCalledWithIdsParameter_GivenProductsWithSomeOfTheSpecifiedIds_ShouldReturnThemSortedById()
        {
            var idsCollection = new List<int>() { 1, 5, 97373, 4 };

            var products = await _productApiService.GetProductsAsync(ids: idsCollection);

            // Assert
            CollectionAssert.IsNotEmpty(products);
            Assert.AreEqual(idsCollection[0], products[0].Id);
            Assert.AreEqual(idsCollection[3], products[1].Id);
            Assert.AreEqual(idsCollection[1], products[2].Id);
        }

        [Test]
        public async Task WhenCalledWithIdsParameter_GivenProductsThatDoNotMatchTheSpecifiedIds_ShouldReturnEmptyCollection()
        {
            var idsCollection = new List<int>() { 2123434, 5456456, 97373, -45 };

            var products = await _productApiService.GetProductsAsync(ids: idsCollection);

            // Assert
            CollectionAssert.IsEmpty(products);
        }

        [Test]
        public async Task WhenCalledWithIdsParameter_GivenEmptyIdsCollection_ShouldReturnAllNotDeletedCategories()
        {
            var idsCollection = new List<int>();

            var products = await _productApiService.GetProductsAsync(ids: idsCollection);

            // Assert
            CollectionAssert.IsNotEmpty(products);
            Assert.AreEqual(products.Count, _existigProducts.Count(x => !x.Deleted));
        }
    }
}