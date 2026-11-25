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
    public class ProductApiServiceTests_GetProducts_LimitParameter
    {
        private IProductApiService _productApiService;
        private List<Product> _existigProducts;

        [SetUp]
        public void Setup()
        {
            _existigProducts = new List<Product>();

            for (int i = 0; i < 1000; i++)
            {
                _existigProducts.Add(new Product()
                {
                    Id = i + 1
                });
            }

            _existigProducts[5].Deleted = true;
            _existigProducts[51].Published = false;

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
        public async Task WhenCalledWithLimitParameter_GivenProductsAboveTheLimit_ShouldReturnCollectionWithCountEqualToTheLimit()
        {
            //Arange
            var expectedLimit = 5;

            //Act
            var products = await _productApiService.GetProductsAsync(limit: expectedLimit);

            // Assert
            CollectionAssert.IsNotEmpty(products);
            Assert.AreEqual(expectedLimit, products.Count);
        }

        [Test]
        public async Task WhenCalledWithLimitParameter_GivenProductsBellowTheLimit_ShouldReturnCollectionWithCountEqualToTheAvailableProducts()
        {
            //Arange
            var expectedLimit = _existigProducts.Count(x => !x.Deleted);

            //Act
            var products = await _productApiService.GetProductsAsync(limit: expectedLimit + 10);

            // Assert
            CollectionAssert.IsNotEmpty(products);
            Assert.AreEqual(expectedLimit, products.Count);
        }

        [Test]
        public async Task WhenCalledWithZeroLimitParameter_GivenSomeProducts_ShouldReturnEmptyCollection()
        {
            //Arange
            var expectedLimit = 0;

            //Act
            var products = await _productApiService.GetProductsAsync(limit: expectedLimit);

            // Assert
            CollectionAssert.IsEmpty(products);
        }

        [Test]
        public async Task WhenCalledWithNegativeLimitParameter_GivenSomeProducts_ShouldReturnEmptyCollection()
        {
            //Arange
            var expectedLimit = -10;

            //Act
            var products = await _productApiService.GetProductsAsync(limit: expectedLimit);

            // Assert
            CollectionAssert.IsEmpty(products);
        }
    }
}