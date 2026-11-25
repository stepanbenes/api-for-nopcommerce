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
    public class ProductApiServiceTests_GetProducts_DefaultParameters
    {
        [Test]
        public async Task WhenCalledWithDefaultParameters_GivenNoProductsExist_ShouldReturnEmptyCollection()
        {
            // Arange
            var productsRepo = Mock.Of<IRepository<Product>>();
            Mock.Get(productsRepo).Setup(x => x.Table).Returns(new List<Product>().AsQueryable());

            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();
            var vendorRepo = Mock.Of<IRepository<Vendor>>();

            var storeMappingService = Mock.Of<IStoreMappingService>();
            Mock.Get(storeMappingService).Setup(x => x.ApplyStoreMapping(It.IsAny<IQueryable<Product>>(), 1))
                .ReturnsAsync((IQueryable<Product> query, int _) => query);
            var storeContext = Mock.Of<Nop.Core.IStoreContext>();
            Mock.Get(storeContext).Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Nop.Core.Domain.Stores.Store { Id = 1 });

            // Act
            var cut = new ProductApiService(productsRepo, productCategoryRepo, vendorRepo, storeMappingService, storeContext);
            var products = await cut.GetProductsAsync();

            // Assert
            CollectionAssert.IsEmpty(products);
        }

        [Test]
        public async Task WhenCalledWithDefaultParameters_GivenOnlyDeletedProductsExist_ShouldReturnEmptyCollection()
        {
            var existingProducts = new List<Product>();
            existingProducts.Add(new Product() { Id = 1, Deleted = true });
            existingProducts.Add(new Product() { Id = 2, Deleted = true });

            // Arange
            var productRepo = Mock.Of<IRepository<Product>>();
            Mock.Get(productRepo).Setup(x => x.Table).Returns(existingProducts.AsQueryable());

            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();
            var vendorRepo = Mock.Of<IRepository<Vendor>>();

            var storeMappingService = Mock.Of<IStoreMappingService>();
            Mock.Get(storeMappingService).Setup(x => x.ApplyStoreMapping(It.IsAny<IQueryable<Product>>(), 1))
                .ReturnsAsync((IQueryable<Product> query, int _) => query);
            var storeContext = Mock.Of<Nop.Core.IStoreContext>();
            Mock.Get(storeContext).Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Nop.Core.Domain.Stores.Store { Id = 1 });

            // Act
            var cut = new ProductApiService(productRepo, productCategoryRepo, vendorRepo, storeMappingService, storeContext);
            var products = await cut.GetProductsAsync();

            // Assert
            CollectionAssert.IsEmpty(products);
        }

        [Test]
        public async Task WhenCalledWithDefaultParameters_GivenSomeNotDeletedProductsExist_ShouldReturnThemSortedById()
        {
            var existingProducts = new List<Product>();
            existingProducts.Add(new Product() { Id = 1 });
            existingProducts.Add(new Product() { Id = 2, Deleted = true });
            existingProducts.Add(new Product() { Id = 3 });

            var expectedCollection = existingProducts.Where(x => !x.Deleted).OrderBy(x => x.Id);

            // Arange
            var productRepo = Mock.Of<IRepository<Product>>();
            Mock.Get(productRepo).Setup(x => x.Table).Returns(existingProducts.AsQueryable());

            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();
            var vendorRepo = Mock.Of<IRepository<Vendor>>();

            var storeMappingService = Mock.Of<IStoreMappingService>();
            Mock.Get(storeMappingService).Setup(x => x.ApplyStoreMapping(It.IsAny<IQueryable<Product>>(), 1))
                .ReturnsAsync((IQueryable<Product> query, int _) => query);
            var storeContext = Mock.Of<Nop.Core.IStoreContext>();
            Mock.Get(storeContext).Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Nop.Core.Domain.Stores.Store { Id = 1 });

            // Act
            var cut = new ProductApiService(productRepo, productCategoryRepo, vendorRepo, storeMappingService, storeContext);
            var products = await cut.GetProductsAsync();

            // Assert
            CollectionAssert.IsNotEmpty(products);
            Assert.AreEqual(expectedCollection.Count(), products.Count);
            Assert.IsTrue(products.Select(x => x.Id).SequenceEqual(expectedCollection.Select(x => x.Id)));
        }

        [Test]
        public async Task WhenCalledWithDefaultParameters_GivenSomeProductsExist_ShouldReturnThemSortedById()
        {
            var existingProducts = new List<Product>();
            existingProducts.Add(new Product() { Id = 2, Published = false });
            existingProducts.Add(new Product() { Id = 3 });
            existingProducts.Add(new Product() { Id = 1 });

            var expectedCollection = existingProducts.Where(x => !x.Deleted).OrderBy(x => x.Id);

            // Arange
            var productRepo = Mock.Of<IRepository<Product>>();
            Mock.Get(productRepo).Setup(x => x.Table).Returns(existingProducts.AsQueryable());

            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();
            var vendorRepo = Mock.Of<IRepository<Vendor>>();

            var storeMappingService = Mock.Of<IStoreMappingService>();
            Mock.Get(storeMappingService).Setup(x => x.ApplyStoreMapping(It.IsAny<IQueryable<Product>>(), 1))
                .ReturnsAsync((IQueryable<Product> query, int _) => query);
            var storeContext = Mock.Of<Nop.Core.IStoreContext>();
            Mock.Get(storeContext).Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Nop.Core.Domain.Stores.Store { Id = 1 });

            // Act
            var cut = new ProductApiService(productRepo, productCategoryRepo, vendorRepo, storeMappingService, storeContext);
            var products = await cut.GetProductsAsync();

            // Assert
            CollectionAssert.IsNotEmpty(products);
            Assert.AreEqual(expectedCollection.Count(), products.Count);
            Assert.IsTrue(products.Select(x => x.Id).SequenceEqual(expectedCollection.Select(x => x.Id)));
        }
    }
}