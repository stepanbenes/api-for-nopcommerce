using System;
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
    public class CategoryApiServiceTests_GetCategoriesCount_UpdatedParameters
    {
        private ICategoryApiService _categoryApiService;
        private List<Category> _existigCategories;
        private DateTime _baseDate;

        [SetUp]
        public void Setup()
        {
            _baseDate = new DateTime(2016, 2, 12);

            _existigCategories = new List<Category>()
            {
                new Category() {Id = 2, UpdatedOnUtc = _baseDate.AddMonths(2) },
                new Category() {Id = 3, UpdatedOnUtc = _baseDate.AddMonths(6) },
                new Category() {Id = 1, UpdatedOnUtc = _baseDate.AddMonths(7) },
                new Category() {Id = 4, UpdatedOnUtc = _baseDate },
                new Category() {Id = 5, UpdatedOnUtc = _baseDate.AddMonths(3) },
                new Category() {Id = 6, Deleted = true, UpdatedOnUtc = _baseDate.AddMonths(10) },
                new Category() {Id = 7, Published = false, UpdatedOnUtc = _baseDate.AddMonths(4) }
            };

            var categoryRepo = Mock.Of<IRepository<Category>>();
            Mock.Get(categoryRepo).Setup(x => x.Table).Returns(_existigCategories.AsQueryable());

            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();

            var storeMappingService = Mock.Of<IStoreMappingService>();
            Mock.Get(storeMappingService).Setup(x => x.AuthorizeAsync(It.IsAny<Category>())).ReturnsAsync(true);

            _categoryApiService = new CategoryApiService(categoryRepo, productCategoryRepo, storeMappingService, Mock.Of<Nop.Services.Catalog.ICategoryService>());
        }

        [Test]
        public async Task WhenCalledWithUpdatedAtMinParameter_GivenSomeCategoriesUpdatedAfterThatDate_ShouldReturnTheirCount()
        {
            // Arange
            DateTime updatedAtMinDate = _baseDate.AddMonths(5);
            var expectedCollection = _existigCategories.Where(x => x.UpdatedOnUtc > updatedAtMinDate && !x.Deleted);
            var expectedCategoriesCount = expectedCollection.Count();

            // Act
            var categoriesCount = await _categoryApiService.GetCategoriesCountAsync(updatedAtMin: updatedAtMinDate);

            // Assert
            Assert.AreEqual(expectedCategoriesCount, categoriesCount);
        }

        [Test]
        public async Task WhenCalledWithUpdatedAtMinParameter_GivenAllCategoriesUpdatedBeforeThatDate_ShouldReturnZero()
        {
            // Arange
            DateTime updatedAtMinDate = _baseDate.AddMonths(7);

            // Act
            var categoriesCount = await _categoryApiService.GetCategoriesCountAsync(updatedAtMin: updatedAtMinDate);

            // Assert
            Assert.AreEqual(0, categoriesCount);
        }

        [Test]
        public async Task WhenCalledWithUpdatedAtMaxParameter_GivenSomeCategoriesUpdatedBeforeThatDate_ShouldReturnTheirCount()
        {
            // Arange
            DateTime updatedAtMaxDate = _baseDate.AddMonths(5);
            var expectedCollection =
                _existigCategories.Where(x => x.UpdatedOnUtc < updatedAtMaxDate && !x.Deleted);
            var expectedCategoriesCount = expectedCollection.Count();

            // Act
            var categoriesCount = await _categoryApiService.GetCategoriesCountAsync(updatedAtMax: updatedAtMaxDate);

            // Assert
            Assert.AreEqual(expectedCategoriesCount, categoriesCount);
        }

        [Test]
        public async Task WhenCalledWithUpdatedAtMaxParameter_GivenAllCategoriesUpdatedAfterThatDate_ShouldReturnZero()
        {
            // Arange
            DateTime updatedAtMaxDate = _baseDate.Subtract(new TimeSpan(365)); // subtract one year

            // Act
            var categoriesCount = await _categoryApiService.GetCategoriesCountAsync(updatedAtMax: updatedAtMaxDate);

            // Assert
            Assert.AreEqual(0, categoriesCount);
        }
    }
}