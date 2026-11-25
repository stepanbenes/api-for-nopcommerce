using System;
using System.Collections.Generic;
using System.Linq;
using Nop.Data;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Api.DataStructures;
using Nop.Plugin.Api.Services;
using NUnit.Framework;
using Moq;

namespace Nop.Plugin.Api.Tests.ServicesTests.ProductCategoryMappings.GetMappings
{
    [TestFixture]
    public class ProductCategoryMappingsApiServiceTests_GetMappings_PageParameter
    {
        private IProductCategoryMappingsApiService _mappingService;
        private List<ProductCategory> _mappings;

        // Here the product and category ids does not matter so we can use the setup method.
        [SetUp]
        public void Setup()
        {
            var randomNumber = new Random();

            _mappings = new List<ProductCategory>();

            for (int i = 0; i < 1000; i++)
            {
                _mappings.Add(new ProductCategory()
                {
                    ProductId = randomNumber.Next(1, 1000),
                    CategoryId = randomNumber.Next(1, 1000)
                });
            }

            _mappings = _mappings.OrderBy(x => x.Id).ToList();
            
            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();
            Mock.Get(productCategoryRepo).Setup(x => x.Table).Returns(_mappings.AsQueryable());

            _mappingService = new ProductCategoryMappingsApiService(productCategoryRepo);
        }

        [Test]
        public async Task WhenCalledWithLimitAndPageParameter_ShouldReturnTheItemsDeterminedByTheLimiAndPageParameters()
        {
            //Arange
            var limit = 5;
            var page = 6;
            var expectedCollection = ApiList<ProductCategory>.FromPage(_mappings.AsQueryable(), page - 1, limit);

            //Act
            var mappings = await _mappingService.GetMappingsAsync(limit: limit, page: page);

            // Assert
            // Not Empty assert is a good practice when you assert something about collection. Because you can get a false positive if the collection is empty.
            CollectionAssert.IsNotEmpty(mappings);
            Assert.AreEqual(expectedCollection.Count(), mappings.Count);
            Assert.IsTrue(mappings.Select(x => new { x.CategoryId, x.ProductId })
                                  .SequenceEqual(expectedCollection.Select(x => new { x.CategoryId, x.ProductId })));
        }

        [Test]
        public async Task WhenCalledWithZeroPageParameterAndSomeLimit_ShouldReturnTheFirstPage()
        {
            //Arange
            var limit = 5;
            var page = 0;
            var expectedCollection = ApiList<ProductCategory>.FromPage(_mappings.AsQueryable(), page - 1, limit);

            //Act
            var mappings = await _mappingService.GetMappingsAsync(limit: limit, page: page);

            // Assert
            // Not Empty assert is a good practice when you assert something about collection. Because you can get a false positive if the collection is empty.
            CollectionAssert.IsNotEmpty(mappings);
            Assert.AreEqual(expectedCollection.Count(), mappings.Count);
            Assert.AreEqual(_mappings.First().Id, mappings.First().Id);
            Assert.IsTrue(mappings.Select(x => new { x.CategoryId, x.ProductId })
                                 .SequenceEqual(expectedCollection.Select(x => new { x.CategoryId, x.ProductId })));
        }

        [Test]
        public async Task WhenCalledWithNegativePageParameterAndSomeLimit_ShouldReturnTheFirstPage()
        {
            //Arange
            var limit = 5;
            var page = -30;
            var expectedCollection = ApiList<ProductCategory>.FromPage(_mappings.AsQueryable(), page - 1, limit);

            //Act
            var mappings = await _mappingService.GetMappingsAsync(limit: limit, page: page);

            // Assert
            // Not Empty assert is a good practice when you assert something about collection. Because you can get a false positive if the collection is empty.
            CollectionAssert.IsNotEmpty(mappings);
            Assert.AreEqual(expectedCollection.Count(), mappings.Count);
            Assert.AreEqual(_mappings.First().Id, mappings.First().Id);
            Assert.IsTrue(mappings.Select(x => new { x.CategoryId, x.ProductId })
                                  .SequenceEqual(expectedCollection.Select(x => new { x.CategoryId, x.ProductId })));
        }

        [Test]
        public async Task WhenCalledWithLimitAndPageParameterWhichExceedTheTotalMappingsCount_ShouldReturnEmptyCollection()
        {
            //Arange
            var limit = 5;
            var page = _mappings.Count / limit + 100;
            
            //Act
            var categories = await _mappingService.GetMappingsAsync(limit: limit, page: page);

            // Assert
            CollectionAssert.IsEmpty(categories);
        }
    }
}