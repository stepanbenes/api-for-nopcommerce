using System.Collections.Generic;
using System.Linq;
using Nop.Data;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Vendors;
using Nop.Plugin.Api.Services;
using NUnit.Framework;
using Moq;

namespace Nop.Plugin.Api.Tests.ServicesTests.Products.GetProductById
{
    using Nop.Services.Stores;

    [TestFixture]
    public class ProductApiServiceTests_GetProductById
    {
        [Test]
        public async Task WhenNullIsReturnedByTheRepository_ShouldReturnNull()
        {
            int productId = 3;

            // Arange
            var productRepo = Mock.Of<IRepository<Product>>();

            Mock.Get(productRepo).Setup(x => x.Table).Returns((new List<Product>()).AsQueryable());

            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();
            var vendorRepo = Mock.Of<IRepository<Vendor>>();

            var storeMappingService = Mock.Of<IStoreMappingService>();
            Mock.Get(storeMappingService).Setup(x => x.ApplyStoreMapping(It.IsAny<IQueryable<Product>>(), 1))
                .ReturnsAsync((IQueryable<Product> query, int _) => query);
            var storeContext = Mock.Of<Nop.Core.IStoreContext>();
            Mock.Get(storeContext).Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Nop.Core.Domain.Stores.Store { Id = 1 });

            // Act
            var cut = new ProductApiService(productRepo, productCategoryRepo, vendorRepo, storeMappingService, storeContext);
            var result = await cut.GetProductByIdAsync(productId);

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        [TestCase(-2)]
        [TestCase(0)]
        public async Task WhenNegativeOrZeroProductIdPassed_ShouldReturnNull(int negativeOrZeroProductId)
        {
            // Aranges
            var productRepoStub = Mock.Of<IRepository<Product>>();
            Mock.Get(productRepoStub).Setup(x => x.Table).Returns((new List<Product>()).AsQueryable());

            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();
            var vendorRepo = Mock.Of<IRepository<Vendor>>();

            var storeMappingService = Mock.Of<IStoreMappingService>();
            Mock.Get(storeMappingService).Setup(x => x.ApplyStoreMapping(It.IsAny<IQueryable<Product>>(), 1))
                .ReturnsAsync((IQueryable<Product> query, int _) => query);
            var storeContext = Mock.Of<Nop.Core.IStoreContext>();
            Mock.Get(storeContext).Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Nop.Core.Domain.Stores.Store { Id = 1 });

            // Act
            var cut = new ProductApiService(productRepoStub, productCategoryRepo, vendorRepo, storeMappingService, storeContext);
            var result = await cut.GetProductByIdAsync(negativeOrZeroProductId);

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        public async Task WhenProductIsReturnedByTheRepository_ShouldReturnTheSameProduct()
        {
            int productId = 3;
            var product = new Product() { Id = 3, Name = "some name" };

            // Arange
            var productRepo = Mock.Of<IRepository<Product>>();

            var list = new List<Product>();
            list.Add(product);

            Mock.Get(productRepo).Setup(x => x.Table).Returns(list.AsQueryable());

            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();
            var vendorRepo = Mock.Of<IRepository<Vendor>>();

            var storeMappingService = Mock.Of<IStoreMappingService>();
            Mock.Get(storeMappingService).Setup(x => x.ApplyStoreMapping(It.IsAny<IQueryable<Product>>(), 1))
                .ReturnsAsync((IQueryable<Product> query, int _) => query);
            var storeContext = Mock.Of<Nop.Core.IStoreContext>();
            Mock.Get(storeContext).Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Nop.Core.Domain.Stores.Store { Id = 1 });

            // Act
            var cut = new ProductApiService(productRepo, productCategoryRepo, vendorRepo, storeMappingService, storeContext);
            var result = await cut.GetProductByIdAsync(productId);

            // Assert
            Assert.AreSame(product, result);
        }
    }
}