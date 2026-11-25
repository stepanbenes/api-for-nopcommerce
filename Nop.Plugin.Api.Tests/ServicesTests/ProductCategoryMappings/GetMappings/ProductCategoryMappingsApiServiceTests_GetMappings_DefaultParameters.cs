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
    public class ProductCategoryMappingsApiServiceTests_GetMappings_DefaultParameters
    {
        [Test]
        public void GivenNonEmptyValidRepositoryWithMoreThanTheMaxItems_ShouldReturnDefaultLimitItems()
        {
            var repo = new List<ProductCategory>();

            var randomNumber = new Random();

            var currentRepoSize = Constants.Configurations.MaxLimit * 2;

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

            var result = cut.GetMappings();

            // Assert
            Assert.IsNotEmpty(result);
            Assert.AreEqual(Constants.Configurations.DefaultLimit, result.Count);
        }

        [Test]
        public void GivenEmptyRepository_ShouldReturnEmptyCollection()
        {
            var repo = new List<ProductCategory>();
           
            // Arange
            var mappingRepo = Mock.Of<IRepository<ProductCategory>>();
            Mock.Get(mappingRepo).Setup(x => x.Table).Returns(repo.AsQueryable());

            // Act
            var cut = new ProductCategoryMappingsApiService(mappingRepo);

            var result = cut.GetMappings();

            // Assert
            Assert.IsEmpty(result);
        }
    }
}