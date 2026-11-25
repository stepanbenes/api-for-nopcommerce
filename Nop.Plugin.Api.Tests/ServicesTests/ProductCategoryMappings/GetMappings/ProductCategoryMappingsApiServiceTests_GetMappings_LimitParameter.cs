using System;
using System.Collections.Generic;
using System.Linq;
using Nop.Data;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Api.Services;
using NUnit.Framework;
using Moq;

namespace Nop.Plugin.Api.Tests.ServicesTests.ProductCategoryMappings.GetMappings
{
    [TestFixture]
    public class ProductCategoryMappingsApiServiceTests_GetMappings_LimitParameter
    {
        private IProductCategoryMappingsApiService _productCategoryMappingsService;
        private List<ProductCategory> _existigMappings;

        // Here the product and category ids does not matter so we can use the setup method.
        [SetUp]
        public void Setup()
        {
            var randomNumber = new Random();

            _existigMappings = new List<ProductCategory>();

            for (int i = 0; i < 1000; i++)
            {
                _existigMappings.Add(new ProductCategory()
                {
                   ProductId = randomNumber.Next(1, 1000),
                   CategoryId = randomNumber.Next(1, 1000),
                });
            }

            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();
            Mock.Get(productCategoryRepo).Setup(x => x.Table).Returns(_existigMappings.AsQueryable());

            _productCategoryMappingsService = new ProductCategoryMappingsApiService(productCategoryRepo);
        }

        [Test]
        public async Task WhenCalledWithLimitParameter_GivenMappingsAboveTheLimit_ShouldReturnCollectionWithCountEqualToTheLimit()
        {
            //Arange
            var expectedLimit = 5;

            //Act
            var categories = await _productCategoryMappingsService.GetMappingsAsync(limit: expectedLimit);

            // Assert
            // Not Empty assert is a good practice when you assert something about collection. Because you can get a false positive if the collection is empty.
            CollectionAssert.IsNotEmpty(categories);
            Assert.AreEqual(expectedLimit, categories.Count);
        }

        [Test]
        public async Task WhenCalledWithLimitParameter_GivenMappingsBellowTheLimit_ShouldReturnCollectionWithCountEqualToTheAvailableCategories()
        {
            //Arange
            var expectedLimit = _existigMappings.Count();

            //Act
            var categories = await _productCategoryMappingsService.GetMappingsAsync(limit: expectedLimit + 10);

            // Assert
            // Not Empty assert is a good practice when you assert something about collection. Because you can get a false positive if the collection is empty.
            CollectionAssert.IsNotEmpty(categories);
            Assert.AreEqual(expectedLimit, categories.Count);
        }

        [Test]
        public async Task WhenCalledWithZeroLimitParameter_GivenSomeMappings_ShouldReturnEmptyCollection()
        {
            //Arange
            var expectedLimit = 0;

            //Act
            var categories = await _productCategoryMappingsService.GetMappingsAsync(limit: expectedLimit);

            // Assert
            CollectionAssert.IsEmpty(categories);
        }

        [Test]
        public async Task WhenCalledWithNegativeLimitParameter_GivenSomeMappings_ShouldReturnEmptyCollection()
        {
            //Arange
            var expectedLimit = -10;

            //Act
            var categories = await _productCategoryMappingsService.GetMappingsAsync(limit: expectedLimit);

            // Assert
            CollectionAssert.IsEmpty(categories);
        }
    }
}