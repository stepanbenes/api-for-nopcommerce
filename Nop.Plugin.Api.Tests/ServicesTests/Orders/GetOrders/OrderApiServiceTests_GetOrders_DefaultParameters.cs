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
    public class OrderApiServiceTests_GetOrders_DefaultParameters
    {
        [Test]
        public async Task WhenCalledWithDefaultParameters_GivenNoOrdersExist_ShouldReturnEmptyCollection()
        {
            // Arange
            var ordersRepo = Mock.Of<IRepository<Order>>();
            Mock.Get(ordersRepo).Setup(x => x.Table).Returns(new List<Order>().AsQueryable());
            
            // Act
            var cut = new OrderApiService(ordersRepo);
            var orders = await cut.GetOrdersAsync();

            // Assert
            CollectionAssert.IsEmpty(orders);
        }

        [Test]
        public async Task WhenCalledWithDefaultParameters_GivenOnlyDeletedOrdersExist_ShouldReturnEmptyCollection()
        {
            var existingOrders = new List<Order>();
            existingOrders.Add(new Order() { Id = 1, Deleted = true });
            existingOrders.Add(new Order() { Id = 2, Deleted = true });

            // Arange
            var orderRepo = Mock.Of<IRepository<Order>>();
            Mock.Get(orderRepo).Setup(x => x.Table).Returns(existingOrders.AsQueryable());
            
            // Act
            var cut = new OrderApiService(orderRepo);
            var orders = await cut.GetOrdersAsync();

            // Assert
            CollectionAssert.IsEmpty(orders);
        }

        [Test]
        public async Task WhenCalledWithDefaultParameters_GivenSomeNotDeletedOrdersExist_ShouldReturnThemSortedById()
        {
            var existingOrders = new List<Order>();
            existingOrders.Add(new Order() { Id = 1 });
            existingOrders.Add(new Order() { Id = 2, Deleted = true });
            existingOrders.Add(new Order() { Id = 3 });

            var expectedCollection = existingOrders.Where(x => !x.Deleted).OrderBy(x => x.Id);

            // Arange
            var orderRepo = Mock.Of<IRepository<Order>>();
            Mock.Get(orderRepo).Setup(x => x.Table).Returns(existingOrders.AsQueryable());
            
            // Act
            var cut = new OrderApiService(orderRepo);
            var orders = await cut.GetOrdersAsync();

            // Assert
            CollectionAssert.IsNotEmpty(orders);
            Assert.AreEqual(expectedCollection.Count(), orders.Count);
            Assert.IsTrue(orders.Select(x => x.Id).SequenceEqual(expectedCollection.Select(x => x.Id)));
        }

        [Test]
        public async Task WhenCalledWithDefaultParameters_GivenSomeOrdersExist_ShouldReturnThemSortedById()
        {
            var existingOrders = new List<Order>();
            existingOrders.Add(new Order() { Id = 2 });
            existingOrders.Add(new Order() { Id = 3 });
            existingOrders.Add(new Order() { Id = 1 });

            var expectedCollection = existingOrders.Where(x => !x.Deleted).OrderBy(x => x.Id);

            // Arange
            var orderRepo = Mock.Of<IRepository<Order>>();
            Mock.Get(orderRepo).Setup(x => x.Table).Returns(existingOrders.AsQueryable());
            
            // Act
            var cut = new OrderApiService(orderRepo);
            var orders = await cut.GetOrdersAsync();

            // Assert
            CollectionAssert.IsNotEmpty(orders);
            Assert.AreEqual(expectedCollection.Count(), orders.Count);
            Assert.IsTrue(orders.Select(x => x.Id).SequenceEqual(expectedCollection.Select(x => x.Id)));
        }
    }
}