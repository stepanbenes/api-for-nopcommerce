using System.Collections.Generic;
using System.Linq;
using Nop.Data;
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
    public class ShoppingCartItemsTests_GetShoppingCartItems_LimitParameter
    {
        private IShoppingCartItemApiService _shoppingCartItemApiService;
        private List<ShoppingCartItem> _existigShoppingCartItems;

        [SetUp]
        public void Setup()
        {
            _existigShoppingCartItems = new List<ShoppingCartItem>();

            for (int i = 1; i <= 1000; i++)
            {
                _existigShoppingCartItems.Add(new ShoppingCartItem()
                {
                    Id = i
                });
            }

            var shoppingCartItemRepo = Mock.Of<IRepository<ShoppingCartItem>>();
            Mock.Get(shoppingCartItemRepo).Setup(x => x.Table).Returns(_existigShoppingCartItems.AsQueryable());

            var storeContext = Mock.Of<IStoreContext>();
            Mock.Get(storeContext).Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Store()
            {
                Id = 0
            });

            _shoppingCartItemApiService = new ShoppingCartItemApiService(shoppingCartItemRepo, storeContext, new ShoppingCartSettings());
        }

        [Test]
        public async Task WhenCalledWithLimitParameter_GivenMoreShoppingCartItemsThanTheLimit_ShouldReturnCollectionWithCountEqualToTheLimit()
        {
            //Arange
            var expectedLimit = 5;

            //Act
            var shoppingCartItems = await _shoppingCartItemApiService.GetShoppingCartItemsAsync(limit: expectedLimit);

            // Assert
            CollectionAssert.IsNotEmpty(shoppingCartItems);
            Assert.AreEqual(expectedLimit, shoppingCartItems.Count);
        }

        [Test]
        public async Task WhenCalledWithLimitParameter_GivenShoppingCartItemsBellowTheLimit_ShouldReturnCollectionWithCountEqualToPassedLimit()
        {
            //Arange
            var limit = Constants.Configurations.MaxLimit + 10;

            //Act
            var shoppingCartItems = await _shoppingCartItemApiService.GetShoppingCartItemsAsync(limit: limit);

            // Assert
            CollectionAssert.IsNotEmpty(shoppingCartItems);
            Assert.AreEqual(limit, shoppingCartItems.Count);
        }

        [Test]
        [TestCase(0)]
        [TestCase(-10)]
        public async Task WhenCalledWithZeroOrNegativeLimitParameter_GivenSomeProducts_ShouldReturnEmptyCollection(int limit)
        {
            //Act
            var shoppingCartItems = await _shoppingCartItemApiService.GetShoppingCartItemsAsync(limit: limit);

            // Assert
            CollectionAssert.IsEmpty(shoppingCartItems);
        }
    }
}