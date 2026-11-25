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
        public void WhenNullIsReturnedByTheRepository_ShouldReturnNull()
        {
            int categoryId = 3;
            
            // Arange
            var categoryRepo = Mock.Of<IRepository<Category>>();
            Mock.Get(categoryRepo).Setup(x => x.Table).Returns((new List<Category>()).AsQueryable());

            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();

            var storeMappingService = Mock.Of<IStoreMappingService>();

            // Act
            var cut = new CategoryApiService(categoryRepo, productCategoryRepo, storeMappingService, Mock.Of<Nop.Services.Catalog.ICategoryService>());
            var result = cut.GetCategoryById(categoryId);

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        [TestCase(-2)]
        [TestCase(0)]
        public void WhenNegativeOrZeroCategoryIdPassed_ShouldReturnNull(int negativeOrZeroCategoryId)
        {
            // Aranges
            var categoryRepoStub = Mock.Of<IRepository<Category>>();
            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();

            var storeMappingService = Mock.Of<IStoreMappingService>();

            // Act
            var cut = new CategoryApiService(categoryRepoStub, productCategoryRepo, storeMappingService, Mock.Of<Nop.Services.Catalog.ICategoryService>());
            var result = cut.GetCategoryById(negativeOrZeroCategoryId);

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        public void WhenCategoryIsReturnedByTheRepository_ShouldReturnTheSameCategory()
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

            // Act
            var cut = new CategoryApiService(categoryRepo, productCategoryRepo, storeMappingService, Mock.Of<Nop.Services.Catalog.ICategoryService>());
            var result = cut.GetCategoryById(categoryId);

            // Assert
            Assert.AreSame(category, result);
        }
    }
}
