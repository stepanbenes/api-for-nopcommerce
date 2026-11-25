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
    public class OrderApiServiceTests_GetOrdersCount_CustomerIdParameter
    {
        private IOrderApiService _orderApiService;
        private List<Order> _existigOrders;

        [SetUp]
        public void Setup()
        {
            _existigOrders = new List<Order>()
            {
                new Order() {Id = 2, CustomerId = 1},
                new Order() {Id = 3, CustomerId = 2},
                new Order() {Id = 1, CustomerId = 2},
                new Order() {Id = 4, CustomerId = 1},
                new Order() {Id = 5, CustomerId = 1},
                new Order() {Id = 6, CustomerId = 2},
                new Order() {Id = 7, CustomerId = 1, Deleted = true}
            };

            var orderRepo = Mock.Of<IRepository<Order>>();
            Mock.Get(orderRepo).Setup(x => x.Table).Returns(_existigOrders.AsQueryable());

            _orderApiService = new OrderApiService(orderRepo);
        }

        [Test]
        public async Task WhenCalledWithValidCustomerId_ShouldReturnTheCountOfTheOrdersForThisCustomer()
        {
            // Arange
            int customerId = 1;
            var expectedCollectionCount = _existigOrders.Count(x => x.CustomerId == customerId && !x.Deleted);

            // Act
            var productsCount = await _orderApiService.GetOrdersCountAsync(customerId: customerId);

            // Assert
            Assert.AreEqual(expectedCollectionCount, productsCount);
        }

        [Test]
        [TestCase(0)]
        [TestCase(-10)]
        public async Task WhenCalledWithNegativeOrZeroCustomerId_ShouldReturnZero(int customerId)
        {
            // Act
            var ordersCount = await _orderApiService.GetOrdersCountAsync(customerId: customerId);

            // Assert
            Assert.AreEqual(0, ordersCount);
        }

        [Test]
        [TestCase(int.MaxValue)]
        [TestCase(int.MinValue)]
        [TestCase(-1)]
        [TestCase(5465464)]
        public async Task WhenCalledWithCustomerIdThatDoesNotExistInTheMappings_ShouldReturnZero(int customerId)
        {
            // Act
            var ordersCount = await _orderApiService.GetOrdersCountAsync(customerId: customerId);

            // Assert
            Assert.AreEqual(0, ordersCount);
        }
    }
}