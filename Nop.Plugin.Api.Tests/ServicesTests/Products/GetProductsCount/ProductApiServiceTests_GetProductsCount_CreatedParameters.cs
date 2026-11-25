using System;
using System.Collections.Generic;
using System.Linq;
using Nop.Data;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Vendors;
using Nop.Plugin.Api.Services;
using NUnit.Framework;
using Moq;

namespace Nop.Plugin.Api.Tests.ServicesTests.Products.GetProductsCount
{
    using Nop.Services.Stores;

    [TestFixture]
    public class ProductApiServiceTests_GetProductsCount_CreatedParameters
    {
        private IProductApiService _productApiService;
        private List<Product> _existigProducts;
        private DateTime _baseDate;

        [SetUp]
        public void Setup()
        {
            _baseDate = new DateTime(2016, 2, 23);

            _existigProducts = new List<Product>()
            {
                new Product() {Id = 2, CreatedOnUtc = _baseDate.AddMonths(2) },
                new Product() {Id = 3, CreatedOnUtc = _baseDate.AddMonths(10) },
                new Product() {Id = 1, CreatedOnUtc = _baseDate.AddMonths(7) },
                new Product() {Id = 4, CreatedOnUtc = _baseDate },
                new Product() {Id = 5, CreatedOnUtc = _baseDate.AddMonths(3) },
                new Product() {Id = 6, Deleted = true, CreatedOnUtc = _baseDate.AddMonths(10) },
                new Product() {Id = 7, Published = false, CreatedOnUtc = _baseDate.AddMonths(4) }
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
        public async Task WhenCalledWithCreatedAtMinParameter_GivenSomeProductsCreatedAfterThatDate_ShouldReturnTheirCount()
        {
            // Arange
            DateTime createdAtMinDate = _baseDate.AddMonths(5);
            var expectedCollection =
                _existigProducts.Where(x => x.CreatedOnUtc > createdAtMinDate && !x.Deleted);
            var expectedProductsCount = expectedCollection.Count();

            // Act
            var productsCount = await _productApiService.GetProductsCountAsync(createdAtMin: createdAtMinDate);

            // Assert
            Assert.AreEqual(expectedProductsCount, productsCount);
        }

        [Test]
        public async Task WhenCalledWithCreatedAtMinParameter_GivenAllCategoriesCreatedBeforeThatDate_ShouldReturnZero()
        {
            // Arange
            DateTime createdAtMinDate = _baseDate.AddMonths(11);

            // Act
            var productsCount = await _productApiService.GetProductsCountAsync(createdAtMin: createdAtMinDate);

            // Assert
            Assert.AreEqual(0, productsCount);
        }

        [Test]
        public async Task WhenCalledWithCreatedAtMaxParameter_GivenSomeProductsCreatedBeforeThatDate_ShouldReturnTheirCount()
        {
            // Arange
            DateTime createdAtMaxDate = _baseDate.AddMonths(5);
            var expectedCollection = _existigProducts.Where(x => x.CreatedOnUtc < createdAtMaxDate && !x.Deleted);
            var expectedProductsCount = expectedCollection.Count();

            // Act
            var productsCount = await _productApiService.GetProductsCountAsync(createdAtMax: createdAtMaxDate);

            // Assert
            Assert.AreEqual(expectedProductsCount, productsCount);
        }

        [Test]
        public async Task WhenCalledWithCreatedAtMaxParameter_GivenAllProductsCreatedAfterThatDate_ShouldReturnZero()
        {
            // Arange
            DateTime createdAtMaxDate = _baseDate.Subtract(new TimeSpan(365)); // subtract one year

            // Act
            var productsCount = await _productApiService.GetProductsCountAsync(createdAtMax: createdAtMaxDate);

            // Assert
            Assert.AreEqual(0, productsCount);
        }
    }
}