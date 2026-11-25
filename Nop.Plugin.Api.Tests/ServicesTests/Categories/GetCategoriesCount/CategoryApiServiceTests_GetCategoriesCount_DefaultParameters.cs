using System.Collections.Generic;
using System.Linq;
using Nop.Data;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Api.Services;
using NUnit.Framework;
using Moq;

namespace Nop.Plugin.Api.Tests.ServicesTests.Categories.GetCategoriesCount
{
    using Nop.Services.Stores;

    [TestFixture]
    public class CategoryApiServiceTests_GetCategoriesCount_DefaultParameters
    {
        [Test]
        public async Task WhenCalledWithDefaultParameters_GivenNoCategoriesExist_ShouldReturnZero()
        {
            // Arange
            var categoryRepo = Mock.Of<IRepository<Category>>();
            Mock.Get(categoryRepo).Setup(x => x.Table).Returns(new List<Category>().AsQueryable());

            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();

            var storeMappingService = Mock.Of<IStoreMappingService>();
            Mock.Get(storeMappingService).Setup(x => x.AuthorizeAsync(It.IsAny<Category>())).ReturnsAsync(true);

            // Act
            var cut = new CategoryApiService(categoryRepo, productCategoryRepo, storeMappingService, Mock.Of<Nop.Services.Catalog.ICategoryService>());
            var categoriesCount = await cut.GetCategoriesCountAsync();

            // Assert
            Assert.AreEqual(0, categoriesCount);
        }
        
        [Test]
        public async Task WhenCalledWithDefaultParameters_GivenOnlyDeletedCategoriesExist_ShouldReturnZero()
        {
            var existingCategories = new List<Category>();
            existingCategories.Add(new Category() { Id = 1, Deleted = true });
            existingCategories.Add(new Category() { Id = 2, Deleted = true });

            // Arange
            var categoryRepo = Mock.Of<IRepository<Category>>();
            Mock.Get(categoryRepo).Setup(x => x.Table).Returns(existingCategories.AsQueryable());

            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();

            var storeMappingService = Mock.Of<IStoreMappingService>();

            // Act
            var cut = new CategoryApiService(categoryRepo, productCategoryRepo, storeMappingService, Mock.Of<Nop.Services.Catalog.ICategoryService>());
            var countResult = await cut.GetCategoriesCountAsync();

            // Assert
            Assert.AreEqual(0, countResult);
        }

        [Test]
        public async Task WhenCalledWithDefaultParameters_GivenSomeNotDeletedCategoriesExist_ShouldReturnTheirCount()
        {
            var existingCategories = new List<Category>();
            existingCategories.Add(new Category() { Id = 1 });
            existingCategories.Add(new Category() { Id = 2, Deleted = true });
            existingCategories.Add(new Category() { Id = 3 });

            // Arange
            var categoryRepo = Mock.Of<IRepository<Category>>();
            Mock.Get(categoryRepo).Setup(x => x.Table).Returns(existingCategories.AsQueryable());

            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();

            var storeMappingService = Mock.Of<IStoreMappingService>();
            Mock.Get(storeMappingService).Setup(x => x.AuthorizeAsync(It.IsAny<Category>())).ReturnsAsync(true);

            // Act
            var cut = new CategoryApiService(categoryRepo, productCategoryRepo, storeMappingService, Mock.Of<Nop.Services.Catalog.ICategoryService>());
            var countResult = await cut.GetCategoriesCountAsync();

            // Assert
            Assert.AreEqual(2, countResult);
        }

        [Test]
        public async Task WhenCalledWithDefaultParameters_GivenSomeCategoriesExist_ShouldReturnTheirCount()
        {
            var existingCategories = new List<Category>();
            existingCategories.Add(new Category() { Id = 2, Published = false });
            existingCategories.Add(new Category() { Id = 3 });
            existingCategories.Add(new Category() { Id = 1 });
            
            // Arange
            var categoryRepo = Mock.Of<IRepository<Category>>();
            Mock.Get(categoryRepo).Setup(x => x.Table).Returns(existingCategories.AsQueryable());

            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();

            var storeMappingService = Mock.Of<IStoreMappingService>();
            Mock.Get(storeMappingService).Setup(x => x.AuthorizeAsync(It.IsAny<Category>())).ReturnsAsync(true);

            // Act
            var cut = new CategoryApiService(categoryRepo, productCategoryRepo, storeMappingService, Mock.Of<Nop.Services.Catalog.ICategoryService>());
            var countResult = await cut.GetCategoriesCountAsync();

            // Assert
            Assert.AreEqual(existingCategories.Count, countResult);
        }
    }
}
