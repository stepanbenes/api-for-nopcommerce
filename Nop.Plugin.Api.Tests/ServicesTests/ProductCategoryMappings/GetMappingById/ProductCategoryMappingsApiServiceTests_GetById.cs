using Nop.Data;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Api.Services;
using NUnit.Framework;
using Moq;

namespace Nop.Plugin.Api.Tests.ServicesTests.ProductCategoryMappings.GetMappingById
{
    [TestFixture]
    public class ProductCategoryMappingsApiServiceTests_GetById
    {
        [Test]
        public async Task WhenNullIsReturnedByTheRepository_ShouldReturnNull()
        {
            int mappingId = 3;
            
            // Arange
            var productCategoryRepo = Mock.Of<IRepository<ProductCategory>>();
            Mock.Get(productCategoryRepo).Setup(x => x.GetByIdAsync(mappingId, null, true, false)).ReturnsAsync((ProductCategory)null);
            
            // Act  
            var cut = new ProductCategoryMappingsApiService(productCategoryRepo);
            var result = await cut.GetByIdAsync(mappingId);

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        [TestCase(-2)]
        [TestCase(0)]
        public async Task WhenNegativeOrZeroMappingIdPassed_ShouldReturnNull(int negativeOrZeroOrderId)
        {
            // Aranges
            var mappingRepoMock = Mock.Of<IRepository<ProductCategory>>();

            // Act
            var cut = new ProductCategoryMappingsApiService(mappingRepoMock);
            var result = await cut.GetByIdAsync(negativeOrZeroOrderId);

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        public async Task WhenMappingIsReturnedByTheRepository_ShouldReturnTheSameMapping()
        {
            int mappingId = 3;
            var mapping = new ProductCategory() { Id = 3 };

            // Arange
            var mappingRepo = Mock.Of<IRepository<ProductCategory>>();
            Mock.Get(mappingRepo).Setup(x => x.GetByIdAsync(mappingId, null, true, false)).ReturnsAsync(mapping);
            
            // Act
            var cut = new ProductCategoryMappingsApiService(mappingRepo);
            var result = await cut.GetByIdAsync(mappingId);

            // Assert
            Assert.AreSame(mapping, result);
        }
    }
}
