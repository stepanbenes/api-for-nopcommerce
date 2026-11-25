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
    public class CategoryApiServiceTests_GetCategories_IdsParameter
    {
        private ICategoryApiService _categoryApiService;
        private List<Category> _existigCategories;

        [SetUp]
        public void Setup()
        {
            _existigCategories = new List<Category>()
            {
                new Category() {Id = 2},
                new Category() {Id = 3},
                new Category() {Id = 1},
                new Category() {Id = 4},
                new Category() {Id = 5},
                new Category() {Id = 6, Deleted = true},
                new Category() {Id = 7, Published = false}
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
        public async Task WhenCalledWithIdsParameter_GivenCategoriesWithTheSpecifiedIds_ShouldReturnThemSortedById()
        {
            var idsCollection = new List<int>() { 1, 5 };

            var categories = await _categoryApiService.GetCategoriesAsync(ids: idsCollection);

            // Assert
            // Not Empty assert is a good practice when you assert something about collection. Because you can get a false positive if the collection is empty.
            CollectionAssert.IsNotEmpty(categories);
            Assert.AreEqual(idsCollection[0], categories[0].Id);
            Assert.AreEqual(idsCollection[1], categories[1].Id);
        }

        [Test]
        public async Task WhenCalledWithIdsParameter_GivenCategoriesWithSomeOfTheSpecifiedIds_ShouldReturnThemSortedById()
        {
            var idsCollection = new List<int>() { 1, 5, 97373, 4 };

            var categories = await _categoryApiService.GetCategoriesAsync(ids: idsCollection);

            // Assert
            // Not Empty assert is a good practice when you assert something about collection. Because you can get a false positive if the collection is empty.
            CollectionAssert.IsNotEmpty(categories);
            Assert.AreEqual(idsCollection[0], categories[0].Id);
            Assert.AreEqual(idsCollection[3], categories[1].Id);
            Assert.AreEqual(idsCollection[1], categories[2].Id);
        }

        [Test]
        public async Task WhenCalledWithIdsParameter_GivenCategoriesThatDoNotMatchTheSpecifiedIds_ShouldReturnEmptyCollection()
        {
            var idsCollection = new List<int>() { 2123434, 5456456, 97373, -45 };

            var categories = await _categoryApiService.GetCategoriesAsync(ids: idsCollection);

            // Assert
            CollectionAssert.IsEmpty(categories);
        }

        [Test]
        public async Task WhenCalledWithIdsParameter_GivenEmptyIdsCollection_ShouldReturnAllNotDeletedCategories()
        {
            var idsCollection = new List<int>();

            var categories = await _categoryApiService.GetCategoriesAsync(ids: idsCollection);

            // Assert
            CollectionAssert.IsNotEmpty(categories);
            Assert.AreEqual(categories.Count, _existigCategories.Count(x => !x.Deleted));
        }
    }
}