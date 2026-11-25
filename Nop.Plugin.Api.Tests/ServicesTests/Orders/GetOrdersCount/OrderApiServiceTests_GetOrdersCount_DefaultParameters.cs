using System.Collections.Generic;
using System.Linq;
using Nop.Data;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Api.Services;
using NUnit.Framework;
using Moq;

namespace Nop.Plugin.Api.Tests.ServicesTests.Orders.GetOrdersCount
{
    [TestFixture]
    public class OrderApiServiceTests_GetOrdersCount_DefaultParameters
    {
        [Test]
        public async Task WhenCalledWithDefaultParameters_GivenNoOrdersExist_ShouldReturnZero()
        {
            // Arange
            var orderRepo = Mock.Of<IRepository<Order>>();
            Mock.Get(orderRepo).Setup(x => x.Table).Returns(new List<Order>().AsQueryable());
            
            // Act
            var cut = new OrderApiService(orderRepo);
            var ordersCount = await cut.GetOrdersCountAsync();

            // Assert
            Assert.AreEqual(0, ordersCount);
        }

        [Test]
        public async Task WhenCalledWithDefaultParameters_GivenOnlyDeletedOrdersExist_ShouldReturnZero()
        {
            var existingOrders = new List<Order>();
            existingOrders.Add(new Order() { Id = 1, Deleted = true });
            existingOrders.Add(new Order() { Id = 2, Deleted = true });

            // Arange
            var orderRepo = Mock.Of<IRepository<Order>>();
            Mock.Get(orderRepo).Setup(x => x.Table).Returns(existingOrders.AsQueryable());
            
            // Act
            var cut = new OrderApiService(orderRepo);
            var countResult = await cut.GetOrdersCountAsync();

            // Assert
            Assert.AreEqual(0, countResult);
        }

        [Test]
        public async Task WhenCalledWithDefaultParameters_GivenSomeNotDeletedOrderExist_ShouldReturnTheirCount()
        {
            var existingOrders = new List<Order>();
            existingOrders.Add(new Order() { Id = 1 });
            existingOrders.Add(new Order() { Id = 2, Deleted = true });
            existingOrders.Add(new Order() { Id = 3 });

            // Arange
            var ordersRepo = Mock.Of<IRepository<Order>>();
            Mock.Get(ordersRepo).Setup(x => x.Table).Returns(existingOrders.AsQueryable());
            
            // Act
            var cut = new OrderApiService(ordersRepo);
            var countResult = await cut.GetOrdersCountAsync();

            // Assert
            Assert.AreEqual(2, countResult);
        }

        [Test]
        public async Task WhenCalledWithDefaultParameters_GivenSomeOrdersExist_ShouldReturnTheirCount()
        {
            var existingOrders = new List<Order>();
            existingOrders.Add(new Order() { Id = 2 });
            existingOrders.Add(new Order() { Id = 3 });
            existingOrders.Add(new Order() { Id = 1 });

            // Arange
            var ordersRepo = Mock.Of<IRepository<Order>>();
            Mock.Get(ordersRepo).Setup(x => x.Table).Returns(existingOrders.AsQueryable());

            // Act
            var cut = new OrderApiService(ordersRepo);
            var countResult = await cut.GetOrdersCountAsync();

            // Assert
            Assert.AreEqual(existingOrders.Count, countResult);
        }
    }
}