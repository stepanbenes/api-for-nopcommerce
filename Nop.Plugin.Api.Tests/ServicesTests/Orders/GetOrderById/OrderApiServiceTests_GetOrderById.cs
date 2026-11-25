using System.Collections.Generic;
using System.Linq;
using Nop.Data;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Api.Services;
using NUnit.Framework;
using Moq;

namespace Nop.Plugin.Api.Tests.ServicesTests.Orders.GetOrderById
{
    [TestFixture]
    public class OrderApiServiceTests_GetOrderById
    {
        [Test]
        public void WhenNullIsReturnedByTheRepository_ShouldReturnNull()
        {
            int orderId = 3;
            
            // Arange
            var orderRepo = Mock.Of<IRepository<Order>>();
            Mock.Get(orderRepo).Setup(x => x.Table).Returns((new List<Order>()).AsQueryable());

            // Act  
            var cut = new OrderApiService(orderRepo);
            var result = cut.GetOrderById(orderId);

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        [TestCase(-2)]
        [TestCase(0)]
        public void WhenNegativeOrZeroOrderIdPassed_ShouldReturnNull(int negativeOrZeroOrderId)
        {
            // Aranges
            var orderRepoStub = Mock.Of<IRepository<Order>>();

            // Act
            var cut = new OrderApiService(orderRepoStub);
            var result = cut.GetOrderById(negativeOrZeroOrderId);

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        public void WhenOrderIsReturnedByTheRepository_ShouldReturnTheSameOrder()
        {
            int orderId = 3;
            var order = new Order() { Id = 3 };

            // Arange
            var orderRepo = Mock.Of<IRepository<Order>>();

            var list = new List<Order>();
            list.Add(order);

            Mock.Get(orderRepo).Setup(x => x.Table).Returns(list.AsQueryable());
            
            // Act
            var cut = new OrderApiService(orderRepo);
            var result = cut.GetOrderById(orderId);

            // Assert
            Assert.AreSame(order, result);
        }
    }
}
