using System.Collections.Generic;
using System.Linq;
using Nop.Data;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Api.DataStructures;
using Nop.Plugin.Api.Services;
using NUnit.Framework;
using Moq;

namespace Nop.Plugin.Api.Tests.ServicesTests.Categories.GetCategories
{
    using Nop.Services.Stores;

    [TestFixture]
    public class CategoryApiServiceTests_GetCategories_PageParameter
    {
        private ICategoryApiService _categoryApiService;
        private List<Category> _existigCategories;

        [SetUp]
        public void Setup()
        {
            _existigCategories = new List<Category>();

            for (int i = 0; i < 1000; i++)
            {
                _existigCategories.Add(new Category()
                {
                    Id = i + 1
                });
            }

            _existigCategories[5].Deleted = true;
            _existigCategories[51].Published = false;
            _existigCategories = _existigCategories.OrderBy(x => x.Id).ToList();

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
        public async Task WhenCalledWithPageParameter_GivenLimitedCategoriesCollection_ShouldReturnThePartOfTheCollectionThatCorrespondsToThePage()
        {
            //Arange
            var limit = 5;
            var page = 6;
            var expectedCollection = ApiList<Category>.FromPage(_existigCategories.Where(x => !x.Deleted).AsQueryable(), page - 1, limit);

            //Act
            var categories = await _categoryApiService.GetCategoriesAsync(limit: limit, page: page);

            // Assert
            // Not Empty assert is a good practice when you assert something about collection. Because you can get a false positive if the collection is empty.
            CollectionAssert.IsNotEmpty(categories);
            Assert.AreEqual(expectedCollection.Count(), categories.Count);
            Assert.IsTrue(categories.Select(x => x.Id).SequenceEqual(expectedCollection.Select(x => x.Id)));
        }

        [Test]
        public async Task WhenCalledWithZeroPageParameter_GivenLimitedCategoriesCollection_ShouldReturnTheFirstPage()
        {
            //Arange
            var limit = 5;
            var page = 0;
            var expectedCollection = ApiList<Category>.FromPage(_existigCategories.Where(x => !x.Deleted).AsQueryable(), page - 1, limit);

            //Act
            var categories = await _categoryApiService.GetCategoriesAsync(limit: limit, page: page);

            // Assert
            // Not Empty assert is a good practice when you assert something about collection. Because you can get a false positive if the collection is empty.
            CollectionAssert.IsNotEmpty(categories);
            Assert.AreEqual(expectedCollection.Count(), categories.Count);
            Assert.AreEqual(_existigCategories.First().Id, categories.First().Id);
            Assert.IsTrue(categories.Select(x => x.Id).SequenceEqual(expectedCollection.Select(x => x.Id)));
        }

        [Test]
        public async Task WhenCalledWithNegativePageParameter_GivenLimitedCategoriesCollection_ShouldReturnTheFirstPage()
        {
            //Arange
            var limit = 5;
            var page = -30;
            var expectedCollection = ApiList<Category>.FromPage(_existigCategories.Where(x => !x.Deleted).AsQueryable(), page - 1, limit);

            //Act
            var categories = await _categoryApiService.GetCategoriesAsync(limit: limit, page: page);

            // Assert
            // Not Empty assert is a good practice when you assert something about collection. Because you can get a false positive if the collection is empty.
            CollectionAssert.IsNotEmpty(categories);
            Assert.AreEqual(expectedCollection.Count(), categories.Count);
            Assert.AreEqual(_existigCategories.First().Id, categories.First().Id);
            Assert.IsTrue(categories.Select(x => x.Id).SequenceEqual(expectedCollection.Select(x => x.Id)));
        }

        [Test]
        public async Task WhenCalledWithTooBigPageParameter_GivenLimitedCategoriesCollection_ShouldReturnEmptyCollection()
        {
            //Arange
            var limit = 5;
            var page = _existigCategories.Count / limit + 100;
            
            //Act
            var categories = await _categoryApiService.GetCategoriesAsync(limit: limit, page: page);

            // Assert
            CollectionAssert.IsEmpty(categories);
        }
    }
}