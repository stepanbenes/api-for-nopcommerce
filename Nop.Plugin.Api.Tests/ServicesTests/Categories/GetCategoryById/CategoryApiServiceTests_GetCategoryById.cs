using System.Collections.Generic;
using System.Linq;
using Nop.Data;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Api.Services;
using NUnit.Framework;
using Moq;

namespace Nop.Plugin.Api.Tests.ServicesTests.Categories.GetCategoryById
{
    using Nop.Services.Stores;

    [TestFixture]
    public class CategoryApiServiceTests_GetCategoryById
    {
        [Test]
        public async Task WhenNullIsReturnedByTheRepository_ShouldReturnNull()
        {
            int categoryId = 3;
            
            // Arange
            var categoryRepo = Mock.Of<IRepository<Category>>();
            Mock.Get(categoryRepo).Setup(x => x.Table).Returns((new List<Category>()).AsQueryable());

            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();

            var storeMappingService = Mock.Of<IStoreMappingService>();
            Mock.Get(storeMappingService).Setup(x => x.ApplyStoreMapping(It.IsAny<IQueryable<Category>>(), 1))
                .ReturnsAsync((IQueryable<Category> query, int _) => query);
            var storeContext = Mock.Of<Nop.Core.IStoreContext>();
            Mock.Get(storeContext).Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Nop.Core.Domain.Stores.Store { Id = 1 });

            // Act
            var cut = new CategoryApiService(categoryRepo, productCategoryRepo, storeMappingService, storeContext, Mock.Of<Nop.Services.Catalog.ICategoryService>());
            var result = await cut.GetCategoryByIdAsync(categoryId);

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        [TestCase(-2)]
        [TestCase(0)]
        public async Task WhenNegativeOrZeroCategoryIdPassed_ShouldReturnNull(int negativeOrZeroCategoryId)
        {
            // Aranges
            var categoryRepoStub = Mock.Of<IRepository<Category>>();
            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();

            var storeMappingService = Mock.Of<IStoreMappingService>();
            Mock.Get(storeMappingService).Setup(x => x.ApplyStoreMapping(It.IsAny<IQueryable<Category>>(), 1))
                .ReturnsAsync((IQueryable<Category> query, int _) => query);
            var storeContext = Mock.Of<Nop.Core.IStoreContext>();
            Mock.Get(storeContext).Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Nop.Core.Domain.Stores.Store { Id = 1 });

            // Act
            var cut = new CategoryApiService(categoryRepoStub, productCategoryRepo, storeMappingService, storeContext, Mock.Of<Nop.Services.Catalog.ICategoryService>());
            var result = await cut.GetCategoryByIdAsync(negativeOrZeroCategoryId);

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        public async Task WhenCategoryIsReturnedByTheRepository_ShouldReturnTheSameCategory()
        {
            int categoryId = 3;
            Category category = new Category() { Id = 3, Name = "some name" };

            // Arange
            var categoryRepo = Mock.Of<IRepository<Category>>();

            var list = new List<Category>();
            list.Add(category);

            Mock.Get(categoryRepo).Setup(x => x.Table).Returns(list.AsQueryable());


            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();

            var storeMappingService = Mock.Of<IStoreMappingService>();
            Mock.Get(storeMappingService).Setup(x => x.ApplyStoreMapping(It.IsAny<IQueryable<Category>>(), 1))
                .ReturnsAsync((IQueryable<Category> query, int _) => query);
            var storeContext = Mock.Of<Nop.Core.IStoreContext>();
            Mock.Get(storeContext).Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Nop.Core.Domain.Stores.Store { Id = 1 });

            // Act
            var cut = new CategoryApiService(categoryRepo, productCategoryRepo, storeMappingService, storeContext, Mock.Of<Nop.Services.Catalog.ICategoryService>());
            var result = await cut.GetCategoryByIdAsync(categoryId);

            // Assert
            Assert.AreSame(category, result);
        }
    }
}
