using System;
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
    public class ShoppingCartItemsTests_GetShoppingCartItems_UpdatedParameters
    {
        private IShoppingCartItemApiService _shoppingCartItemsApiService;
        private List<ShoppingCartItem> _existigShoppingCartItems;
        private DateTime _baseDate;

        [SetUp]
        public void Setup()
        {
            _baseDate = new DateTime(2016, 2, 23);

            _existigShoppingCartItems = new List<ShoppingCartItem>();
            var randomNumber = new Random();

            for (int i = 1; i <= 1000; i++)
            {
                _existigShoppingCartItems.Add(new ShoppingCartItem()
                {
                    Id = i,
                    UpdatedOnUtc = _baseDate.AddMonths(randomNumber.Next(1, 10))
                });
            }

            var shoppingCartItemsRepo = Mock.Of<IRepository<ShoppingCartItem>>();
            Mock.Get(shoppingCartItemsRepo).Setup(x => x.Table).Returns(_existigShoppingCartItems.AsQueryable());

            var storeContext = Mock.Of<IStoreContext>();
            Mock.Get(storeContext).Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Store()
            {
                Id = 0
            });

            _shoppingCartItemsApiService = new ShoppingCartItemApiService(shoppingCartItemsRepo, storeContext, new ShoppingCartSettings());
        }

        [Test]
        public async Task WhenCalledWithUpdatedAtMinParameter_GivenSomeShoppingCartItemsUpdatedAfterThatDate_ShouldReturnThemSortedById()
        {
            // Arange
            DateTime updatedAtMinDate = _baseDate.AddMonths(5);
            
            // Ensure that the date will be in the collection because in the setup method we are using a random number to generate the dates.
            _existigShoppingCartItems.Add(new ShoppingCartItem()
            {
                Id = _existigShoppingCartItems.Count + 1,
                UpdatedOnUtc = updatedAtMinDate
            });

            var expectedCollection = _existigShoppingCartItems.Where(x => x.UpdatedOnUtc > updatedAtMinDate).OrderBy(x => x.Id).Take(Constants.Configurations.DefaultLimit);
            var expectedShoppingCartItemsCount = expectedCollection.Count();

            // Act
            var result = await _shoppingCartItemsApiService.GetShoppingCartItemsAsync(updatedAtMin: updatedAtMinDate);

            // Assert
            Assert.AreEqual(expectedShoppingCartItemsCount, result.Count);
            Assert.IsTrue(result.Select(x => x.Id).SequenceEqual(expectedCollection.Select(x => x.Id)));
        }

        [Test]
        public async Task WhenCalledWithUpdatedAtMinParameter_GivenAllShoppingCartItemsUpdatedBeforeThatDate_ShouldReturnEmptyCollection()
        {
            // Arange
            DateTime updatedAtMinDate = _baseDate.AddMonths(11);

            // Ensure that the date will be in the collection because in the setup method we are using a random number to generate the dates.
            _existigShoppingCartItems.Add(new ShoppingCartItem()
            {
                Id = _existigShoppingCartItems.Count + 1,
                UpdatedOnUtc = updatedAtMinDate
            });

            // Act
            var result = await _shoppingCartItemsApiService.GetShoppingCartItemsAsync(updatedAtMin: updatedAtMinDate);

            // Assert
            CollectionAssert.IsEmpty(result);
        }

        [Test]
        public async Task WhenCalledWithUpdatedAtMaxParameter_GivenSomeShoppingCartItemsUpdatedBeforeThatDate_ShouldReturnThemSortedById()
        {
            // Arange
            DateTime updatedAtMaxDate = _baseDate.AddMonths(5);

            // Ensure that the date will be in the collection because in the setup method we are using a random number to generate the dates.
            _existigShoppingCartItems.Add(new ShoppingCartItem()
            {
                Id = _existigShoppingCartItems.Count + 1,
                UpdatedOnUtc = updatedAtMaxDate
            });

            var expectedCollection =
                _existigShoppingCartItems.Where(x => x.UpdatedOnUtc < updatedAtMaxDate).OrderBy(x => x.Id).Take(Constants.Configurations.DefaultLimit);

            var expectedShoppingCartItemsCount = expectedCollection.Count();

            // Act
            var shoppingCartItems = await _shoppingCartItemsApiService.GetShoppingCartItemsAsync(updatedAtMax: updatedAtMaxDate);

            // Assert
            Assert.AreEqual(expectedShoppingCartItemsCount, shoppingCartItems.Count);
            Assert.IsTrue(shoppingCartItems.Select(x => x.Id).SequenceEqual(expectedCollection.Select(x => x.Id)));
        }

        [Test]
        public async Task WhenCalledWithUpdatedAtMaxParameter_GivenAllProductsUpdatedAfterThatDate_ShouldReturnEmptyCollection()
        {
            // Arange
            DateTime updatedAtMaxDate = _baseDate.Subtract(new TimeSpan(365)); // subtract one year
            
            // Act
            var result = await _shoppingCartItemsApiService.GetShoppingCartItemsAsync(updatedAtMax: updatedAtMaxDate);

            // Assert
            CollectionAssert.IsEmpty(result);
        }
    }
}