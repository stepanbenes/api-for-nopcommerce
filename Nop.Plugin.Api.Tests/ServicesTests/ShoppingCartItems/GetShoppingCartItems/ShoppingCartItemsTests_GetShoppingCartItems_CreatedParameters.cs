using System;
using System.Collections.Generic;
using System.Linq;
using Nop.Data;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Vendors;
using Nop.Plugin.Api.Infrastructure;
using Nop.Plugin.Api.Services;
using NUnit.Framework;
using Moq;

namespace Nop.Plugin.Api.Tests.ServicesTests.ShoppingCartItems.GetShoppingCartItems
{
    using Nop.Core;
    using Nop.Core.Domain.Stores;

    [TestFixture]
    public class ShoppingCartItemsTests_GetShoppingCartItems_CreatedParameters
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
                    CreatedOnUtc = _baseDate.AddMonths(randomNumber.Next(1, 10))
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
        public async Task WhenCalledWithCreatedAtMinParameter_GivenSomeShoppingCartItemsCreatedAfterThatDate_ShouldReturnThemSortedById()
        {
            // Arange
            DateTime createdAtMinDate = _baseDate.AddMonths(5);

            // Ensure that the date will be in the collection because in the setup method we are using a random number to generate the dates.
            _existigShoppingCartItems.Add(new ShoppingCartItem()
            {
                Id = _existigShoppingCartItems.Count + 1,
                CreatedOnUtc = createdAtMinDate
            });

            var expectedCollection =
                _existigShoppingCartItems.Where(x => x.CreatedOnUtc > createdAtMinDate).OrderBy(x => x.Id).Take(Constants.Configurations.DefaultLimit);

            var expectedProductsCount = expectedCollection.Count();

            // Act
            var shoppingCartItems = await _shoppingCartItemsApiService.GetShoppingCartItemsAsync(createdAtMin: createdAtMinDate);

            // Assert
            CollectionAssert.IsNotEmpty(shoppingCartItems);
            Assert.AreEqual(expectedProductsCount, shoppingCartItems.Count);
            Assert.IsTrue(shoppingCartItems.Select(x => x.Id).SequenceEqual(expectedCollection.Select(x => x.Id)));
        }

        [Test]
        public async Task WhenCalledWithCreatedAtMinParameter_GivenAllShoppingCartItemsCreatedBeforeThatDate_ShouldReturnEmptyCollection()
        {
            // Arange
            DateTime createdAtMinDate = _baseDate.AddMonths(11);

            // Ensure that the date will be in the collection because in the setup method we are using a random number to generate the dates.
            _existigShoppingCartItems.Add(new ShoppingCartItem()
            {
                Id = _existigShoppingCartItems.Count + 1,
                CreatedOnUtc = createdAtMinDate
            });

            // Act
            var shoppingCartItems = await _shoppingCartItemsApiService.GetShoppingCartItemsAsync(createdAtMin: createdAtMinDate);

            // Assert
            CollectionAssert.IsEmpty(shoppingCartItems);
        }

        [Test]
        public async Task WhenCalledWithCreatedAtMaxParameter_GivenSomeShoppingCartItemsCreatedBeforeThatDate_ShouldReturnThemSortedById()
        {
            // Arange
            DateTime createdAtMaxDate = _baseDate.AddMonths(5);

            // Ensure that the date will be in the collection because in the setup method we are using a random number to generate the dates.
            _existigShoppingCartItems.Add(new ShoppingCartItem()
            {
                Id = _existigShoppingCartItems.Count + 1,
                CreatedOnUtc = createdAtMaxDate
            });

            var expectedCollection = _existigShoppingCartItems.Where(x => x.CreatedOnUtc < createdAtMaxDate).OrderBy(x => x.Id).Take(Constants.Configurations.DefaultLimit);
            var expectedShoppingCartItemsCount = expectedCollection.Count();

            // Act
            var shoppingCartItems = await _shoppingCartItemsApiService.GetShoppingCartItemsAsync(createdAtMax: createdAtMaxDate);

            // Assert
            CollectionAssert.IsNotEmpty(shoppingCartItems);
            Assert.AreEqual(expectedShoppingCartItemsCount, shoppingCartItems.Count);
            Assert.IsTrue(shoppingCartItems.Select(x => x.Id).SequenceEqual(expectedCollection.Select(x => x.Id)));
        }

        [Test]
        public async Task WhenCalledWithCreatedAtMaxParameter_GivenAllShoppingCartItemsCreatedAfterThatDate_ShouldReturnEmptyCollection()
        {
            // Arange
            DateTime createdAtMaxDate = _baseDate.Subtract(new TimeSpan(365)); // subtract one year
            
            // Act
            var shoppingCartItems = await _shoppingCartItemsApiService.GetShoppingCartItemsAsync(createdAtMax: createdAtMaxDate);

            // Assert
            CollectionAssert.IsEmpty(shoppingCartItems);
        }
    }
}