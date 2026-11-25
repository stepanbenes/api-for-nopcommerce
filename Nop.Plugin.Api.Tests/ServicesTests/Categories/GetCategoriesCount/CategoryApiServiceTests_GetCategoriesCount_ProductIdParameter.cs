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
    public class CategoryApiServiceTests_GetCategoriesCount_ProductIdParameter
    {
        private ICategoryApiService _categoryApiService;
        private List<Category> _existigCategories;
        private List<ProductCategory> _existingCategoryMappings;

        [SetUp]
        public void Setup()
        {
            _existigCategories = new List<Category>()
            {
                new Category() {Id = 2 },
                new Category() {Id = 3 },
                new Category() {Id = 1 },
                new Category() {Id = 4 },
                new Category() {Id = 5 },
                new Category() {Id = 6, Deleted = true },
                new Category() {Id = 7 }
            };

            var categoryRepo = Mock.Of<IRepository<Category>>();
            Mock.Get(categoryRepo).Setup(x => x.Table).Returns(_existigCategories.AsQueryable());

            _existingCategoryMappings= new List<ProductCategory>()
            {
                new ProductCategory() { CategoryId = 1, ProductId = 2 }, 
                new ProductCategory() { CategoryId = 1, ProductId = 3 },
                new ProductCategory() { CategoryId = 2, ProductId = 2 },
                new ProductCategory() { CategoryId = 3, ProductId = 1 },
                new ProductCategory() { CategoryId = 4, ProductId = 4 },
                new ProductCategory() { CategoryId = 5, ProductId = 5 }
            };

            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();
            Mock.Get(productCategoryRepo).Setup(x => x.Table).Returns(_existingCategoryMappings.AsQueryable());

            var storeMappingService = Mock.Of<IStoreMappingService>();
            Mock.Get(storeMappingService).Setup(x => x.AuthorizeAsync(It.IsAny<Category>())).ReturnsAsync(true);

            _categoryApiService = new CategoryApiService(categoryRepo, productCategoryRepo, storeMappingService, Mock.Of<Nop.Services.Catalog.ICategoryService>());
        }

        [Test]
        public async Task WhenCalledWithValidProductId_ShouldReturnOnlyTheCountOfTheCategoriesMappedToThisProduct()
        {
            // Arange
            int productId = 3;
            var expectedCollectionCount = (from cat in _existigCategories
                                          join mapping in _existingCategoryMappings on cat.Id equals mapping.CategoryId
                                          where mapping.ProductId == productId
                                          orderby cat.Id
                                          select cat).Count();

            // Act
            var categoriesCount = await _categoryApiService.GetCategoriesCountAsync(productId: productId);

            // Assert
            Assert.AreEqual(expectedCollectionCount, categoriesCount);
        }

        [Test]
        [TestCase(0)]
        [TestCase(-10)]
        public async Task WhenCalledWithNegativeOrZeroProductId_ShouldReturnZero(int productId)
        {
            // Act
            var categoriesCount = await _categoryApiService.GetCategoriesCountAsync(productId: productId);

            // Assert
            Assert.AreEqual(0, categoriesCount);
        }

        [Test]
        [TestCase(int.MaxValue)]
        [TestCase(int.MinValue)]
        [TestCase(-1)]
        [TestCase(5465464)]
        public async Task WhenCalledWithProductIdThatDoesNotExistInTheMappings_ShouldReturnZero(int productId)
        {
            // Act
            var categoriesCount = await _categoryApiService.GetCategoriesCountAsync(productId: productId);

            // Assert
            Assert.AreEqual(0, categoriesCount);
        }
    }
}