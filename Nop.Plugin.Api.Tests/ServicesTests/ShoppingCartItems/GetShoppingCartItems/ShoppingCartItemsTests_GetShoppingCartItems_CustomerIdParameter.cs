using System;
using System.Collections.Generic;
using System.Linq;
using Nop.Data;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Api.Infrastructure;
using Nop.Plugin.Api.Services;
using NUnit.Framework;
using Moq;

namespace Nop.Plugin.Api.Tests.ServicesTests.ShoppingCartItems.GetShoppingCartItems
{
    using Nop.Core;
    using Nop.Core.Domain.Stores;

    [TestFixture]
    public class ShoppingCartItemsTests_GetShoppingCartItems_CustomerIdParameter
    {
        private IShoppingCartItemApiService _shoppingCartItemApiService;
        private List<ShoppingCartItem> _shoppingCartItems;

        [SetUp]
        public void Setup()
        {
            var randomNumber = new Random();

            _shoppingCartItems = new List<ShoppingCartItem>();

            for (int i = 1; i <= 1000; i++)
            {
                _shoppingCartItems.Add(new ShoppingCartItem()
                {
                    Id = i,
                    CustomerId = randomNumber.Next(1, 10)
                });
            }

            var shoppingCartItemsRepo = Mock.Of<IRepository<ShoppingCartItem>>();
            Mock.Get(shoppingCartItemsRepo).Setup(x => x.Table).Returns(_shoppingCartItems.AsQueryable());

            var storeContext = Mock.Of<IStoreContext>();
            Mock.Get(storeContext).Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Store()
            {
                Id = 0
            });

            _shoppingCartItemApiService = new ShoppingCartItemApiService(shoppingCartItemsRepo, storeContext, new ShoppingCartSettings());
        }
        
        [Test]
        public async Task WhenPassedPositiveCustomerId_GivenShoppingCartItemsForThisCustomer_ShouldReturnOnlyTheShoppingCartItemsForThisCustomerSortedById()
        {
            // Arange
            int customerId = 5;
            var expectedResult = _shoppingCartItems.Where(x => x.CustomerId == customerId).OrderBy(x => x.Id).Take(Constants.Configurations.DefaultLimit);

            // Act 
            var result = await _shoppingCartItemApiService.GetShoppingCartItemsAsync(customerId);

            // Assert
            Assert.IsTrue(expectedResult.Select(x => new {x.Id, x.CustomerId})
                                        .SequenceEqual(result.Select(x => new {x.Id, x.CustomerId})));
        }
        
        [Test]
        [TestCase(0)]
        [TestCase(-10)]
        public async Task WhenPassedNegativeOrZeroCustomerId_ShouldReturnEmptyCollection(int customerId)
        {
            // Act 
            var result = await _shoppingCartItemApiService.GetShoppingCartItemsAsync(customerId);

            // Assert
            Assert.IsEmpty(result);
        }
        
        [Test]
        public async Task WhenPassedNonExistentCustomerId_ShouldReturnEmptyCollection()
        {
            // Arange
            int nonExistendCustomerId = int.MaxValue;

            // Act 
            var result = await _shoppingCartItemApiService.GetShoppingCartItemsAsync(nonExistendCustomerId);

            // Assert
            Assert.IsEmpty(result);
        }
    }
}