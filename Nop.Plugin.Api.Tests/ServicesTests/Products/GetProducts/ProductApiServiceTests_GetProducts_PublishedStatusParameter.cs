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
    public class ProductApiServiceTests_GetProducts_PublishedStatusParameter
    {
        private IProductApiService _productApiService;
        private List<Product> _existigProducts;

        [SetUp]
        public void Setup()
        {
            _existigProducts = new List<Product>()
            {
                new Product() {Id = 2, Published = true },
                new Product() {Id = 3, Published = true  },
                new Product() {Id = 1, Published = false  },
                new Product() {Id = 4, Published = true  },
                new Product() {Id = 5, Published = true  },
                new Product() {Id = 6, Deleted = true, Published = true  },
                new Product() {Id = 7, Published = false }
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
        public async Task WhenAskForOnlyThePublishedProducts_ShouldReturnOnlyThePublishedProductsOrderedByid()
        {
            // Arange
            var expectedProducts = _existigProducts.Where(x => x.Published && !x.Deleted).OrderBy(x => x.Id);

            // Act
            var products = await _productApiService.GetProductsAsync(publishedStatus: true);

            // Assert
            CollectionAssert.IsNotEmpty(products);
            Assert.AreEqual(expectedProducts.Count(), products.Count);
            Assert.IsTrue(products.Select(x => x.Id).SequenceEqual(expectedProducts.Select(x => x.Id)));
        }

        [Test]
        public async Task WhenAskForOnlyTheUnpublishedProducts_ShouldReturnOnlyTheUnpublishedProductsCount()
        {
            // Arange
            var expectedCollection = _existigProducts.Where(x => !x.Published && !x.Deleted).OrderBy(x => x.Id);

            // Act
            var products = await _productApiService.GetProductsAsync(publishedStatus: false);

            // Assert
            CollectionAssert.IsNotEmpty(products);
            Assert.AreEqual(expectedCollection.Count(), products.Count);
            Assert.IsTrue(products.Select(x => x.Id).SequenceEqual(expectedCollection.Select(x => x.Id)));
        }
    }
}