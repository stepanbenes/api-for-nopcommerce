using System;
using System.Collections.Generic;
using System.Linq;
using Nop.Data;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Api.Infrastructure;
using Nop.Plugin.Api.Services;
using NUnit.Framework;
using Moq;

namespace Nop.Plugin.Api.Tests.ServicesTests.ProductCategoryMappings.GetMappings
{
    [TestFixture]
    public class ProductCategoryMappingsApiServiceTests_GetMappings_CategoryIdParameter
    {
        [Test]
        [TestCase(0)]
        [TestCase(-132340)]
        public async Task GivenNegativeOrZeroCategoryId_ShouldReturnEmptyCollection(int categoryId)
        {
            var repo = new List<ProductCategory>();

            var randomNumber = new Random();

            var currentRepoSize = randomNumber.Next(10, 100);

            for (int i = 0; i < currentRepoSize; i++)
            {
                repo.Add(new ProductCategory()
                {
                    CategoryId = randomNumber.Next(10, 20),
                    ProductId = randomNumber.Next(1, 2),
                });
            }

            // Arange
            var mappingRepo = Mock.Of<IRepository<ProductCategory>>();
            Mock.Get(mappingRepo).Setup(x => x.Table).Returns(repo.AsQueryable());

            // Act
            var cut = new ProductCategoryMappingsApiService(mappingRepo);

            var result = await cut.GetMappingsAsync(categoryId: categoryId);

            // Assert
            Assert.IsEmpty(result);
        }

        [Test]
        public async Task GivenPositiveCategoryId_ShouldReturnCollectionContainingAllMappingsWithThisCategoryId()
        {
            var repo = new List<ProductCategory>();

            var randomNumber = new Random();

            var currentRepoSize = randomNumber.Next(51, 100);
            
            for (int i = 0; i < currentRepoSize; i++)
            {
                repo.Add(new ProductCategory()
                {
                    CategoryId = randomNumber.Next(1, 2),
                    ProductId = randomNumber.Next(10, 20),
                });
            }

            var categoryId = 1;

            repo.Add(new ProductCategory()
            {
                CategoryId = categoryId,
                ProductId = randomNumber.Next(10, 20),
            });

            // Arange
            var mappingRepo = Mock.Of<IRepository<ProductCategory>>();
            Mock.Get(mappingRepo).Setup(x => x.Table).Returns(repo.AsQueryable());

            // Act
            var cut = new ProductCategoryMappingsApiService(mappingRepo);

            var result = await cut.GetMappingsAsync(categoryId: categoryId);

            // Assert
            Assert.IsTrue(result.Select(x => new { x.CategoryId, x.ProductId })
                                .SequenceEqual(repo.Where(x => x.CategoryId == categoryId).Take(Constants.Configurations.DefaultLimit).Select(x => new { x.CategoryId, x.ProductId })));
        }
    }
}