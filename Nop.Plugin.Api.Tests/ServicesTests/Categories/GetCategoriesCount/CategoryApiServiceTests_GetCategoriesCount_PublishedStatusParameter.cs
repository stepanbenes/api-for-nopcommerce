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
    public class CategoryApiServiceTests_GetCategoriesCount_PublishedStatusParameter
    {
        private ICategoryApiService _categoryApiService;
        private List<Category> _existigCategories;

        [SetUp]
        public void Setup()
        {
            _existigCategories = new List<Category>()
            {
                new Category() {Id = 2, Published = true },
                new Category() {Id = 3, Published = true  },
                new Category() {Id = 1, Published = false  },
                new Category() {Id = 4, Published = true  },
                new Category() {Id = 5, Published = true  },
                new Category() {Id = 6, Deleted = true, Published = true  },
                new Category() {Id = 7, Published = false }
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
        public async Task WhenAskForOnlyThePublishedCategories_ShouldReturnOnlyThePublishedCategoriesCount()
        {
            // Arange
            var expectedCategoriesCount = _existigCategories.Count(x => x.Published && !x.Deleted);

            // Act
            var categoriesCount = await _categoryApiService.GetCategoriesCountAsync(publishedStatus: true);

            // Assert
            Assert.AreEqual(expectedCategoriesCount, categoriesCount);
        }

        [Test]
        public async Task WhenAskForOnlyTheUnpublishedCategories_ShouldReturnOnlyTheUnpublishedCategoriesCount()
        {
            // Arange
            var expectedCollectionCount = _existigCategories.Count(x => !x.Published && !x.Deleted);

            // Act
            var categoriesCount = await _categoryApiService.GetCategoriesCountAsync(publishedStatus: false);

            // Assert
            Assert.AreEqual(expectedCollectionCount, categoriesCount);
        }
    }
}