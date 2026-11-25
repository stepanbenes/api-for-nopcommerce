using System;
using System.Collections.Generic;
using System.Linq;
using Nop.Data;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Api.Services;
using NUnit.Framework;
using Moq;

namespace Nop.Plugin.Api.Tests.ServicesTests.Categories.GetCategories
{
    using Nop.Services.Stores;

    [TestFixture]
    public class CategoryApiServiceTests_GetCategories_UpdatedParameters
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
            Mock.Get(storeMappingService).Setup(x => x.ApplyStoreMapping(It.IsAny<IQueryable<Category>>(), 1))
                .ReturnsAsync((IQueryable<Category> query, int _) => query);
            var storeContext = Mock.Of<Nop.Core.IStoreContext>();
            Mock.Get(storeContext).Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Nop.Core.Domain.Stores.Store { Id = 1 });

            _categoryApiService = new CategoryApiService(categoryRepo, productCategoryRepo, storeMappingService, storeContext, Mock.Of<Nop.Services.Catalog.ICategoryService>());
        }

        [Test]
        public async Task WhenCalledWithUpdatedAtMinParameter_GivenSomeCategoriesUpdatedAfterThatDate_ShouldReturnThemSortedById()
        {
            // Arange
            DateTime updatedAtMinDate = _baseDate.AddMonths(5);
            var expectedCollection = _existigCategories.Where(x => x.UpdatedOnUtc > updatedAtMinDate && !x.Deleted).OrderBy(x => x.Id);
            var expectedCategoriesCount = expectedCollection.Count();

            // Act
            var categories = await _categoryApiService.GetCategoriesAsync(updatedAtMin: updatedAtMinDate);

            // Assert
            // Not Empty assert is a good practice when you assert something about collection. Because you can get a false positive if the collection is empty.
            CollectionAssert.IsNotEmpty(categories);
            Assert.AreEqual(expectedCategoriesCount, categories.Count);
            Assert.IsTrue(categories.Select(x => x.Id).SequenceEqual(expectedCollection.Select(x => x.Id)));
        }

        [Test]
        public async Task WhenCalledWithUpdatedAtMinParameter_GivenAllCategoriesUpdatedBeforeThatDate_ShouldReturnEmptyCollection()
        {
            // Arange
            DateTime updatedAtMinDate = _baseDate.AddMonths(7);

            // Act
            var categories = await _categoryApiService.GetCategoriesAsync(updatedAtMin: updatedAtMinDate);

            // Assert
            CollectionAssert.IsEmpty(categories);
        }

        [Test]
        public async Task WhenCalledWithUpdatedAtMaxParameter_GivenSomeCategoriesUpdatedBeforeThatDate_ShouldReturnThemSortedById()
        {
            // Arange
            DateTime updatedAtMaxDate = _baseDate.AddMonths(5);
            var expectedCollection =
                _existigCategories.Where(x => x.UpdatedOnUtc < updatedAtMaxDate && !x.Deleted).OrderBy(x => x.Id);
            var expectedCategoriesCount = expectedCollection.Count();

            // Act
            var categories = await _categoryApiService.GetCategoriesAsync(updatedAtMax: updatedAtMaxDate);

            // Assert
            // Not Empty assert is a good practice when you assert something about collection. Because you can get a false positive if the collection is empty.
            CollectionAssert.IsNotEmpty(categories);
            Assert.AreEqual(expectedCategoriesCount, categories.Count);
            Assert.IsTrue(categories.Select(x => x.Id).SequenceEqual(expectedCollection.Select(x => x.Id)));
        }

        [Test]
        public async Task WhenCalledWithUpdatedAtMaxParameter_GivenAllCategoriesUpdatedAfterThatDate_ShouldReturnEmptyCollection()
        {
            // Arange
            DateTime updatedAtMaxDate = _baseDate.Subtract(new TimeSpan(365)); // subtract one year

            // Act
            var categories = await _categoryApiService.GetCategoriesAsync(updatedAtMax: updatedAtMaxDate);

            // Assert
            CollectionAssert.IsEmpty(categories);
        }
    }
}