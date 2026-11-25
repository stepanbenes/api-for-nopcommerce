using System.Collections.Generic;
using System.Linq;
using Nop.Data;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Api.Services;
using NUnit.Framework;
using Moq;

namespace Nop.Plugin.Api.Tests.ServicesTests.Orders.GetOrders
{
    [TestFixture]
    public class OrderApiServiceTests_GetOrders_LimitParameter
    {
        private IOrderApiService _orderApiService;
        private List<Order> _existigOrders;

        [SetUp]
        public void Setup()
        {
            _existigOrders = new List<Order>();

            for (int i = 0; i < 1000; i++)
            {
                _existigOrders.Add(new Order()
                {
                    Id = i + 1
                });
            }

            _existigOrders[5].Deleted = true;

            var orderRepo = Mock.Of<IRepository<Order>>();
            Mock.Get(orderRepo).Setup(x => x.Table).Returns(_existigOrders.AsQueryable());
            
            _orderApiService = new OrderApiService(orderRepo);
        }

        [Test]
        public void WhenCalledWithLimitParameter_GivenOrdersAboveTheLimit_ShouldReturnCollectionWithCountEqualToTheLimit()
        {
            //Arange
            var expectedLimit = 5;

            //Act
            var orders = _orderApiService.GetOrders(limit: expectedLimit);

            // Assert
            CollectionAssert.IsNotEmpty(orders);
            Assert.AreEqual(expectedLimit, orders.Count);
        }

        [Test]
        public void WhenCalledWithLimitParameter_GivenOrdersBellowTheLimit_ShouldReturnCollectionWithCountEqualToTheAvailableOrders()
        {
            //Arange
            var expectedLimit = _existigOrders.Count(x => !x.Deleted);

            //Act
            var orders = _orderApiService.GetOrders(limit: expectedLimit + 10);

            // Assert
            CollectionAssert.IsNotEmpty(orders);
            Assert.AreEqual(expectedLimit, orders.Count);
        }

        [Test]
        public void WhenCalledWithZeroLimitParameter_GivenSomeOrders_ShouldReturnEmptyCollection()
        {
            //Arange
            var expectedLimit = 0;

            //Act
            var orders = _orderApiService.GetOrders(limit: expectedLimit);

            // Assert
            CollectionAssert.IsEmpty(orders);
        }

        [Test]
        public void WhenCalledWithNegativeLimitParameter_GivenSomeOrders_ShouldReturnEmptyCollection()
        {
            //Arange
            var expectedLimit = -10;

            //Act
            var orders = _orderApiService.GetOrders(limit: expectedLimit);

            // Assert
            CollectionAssert.IsEmpty(orders);
        }
    }
}