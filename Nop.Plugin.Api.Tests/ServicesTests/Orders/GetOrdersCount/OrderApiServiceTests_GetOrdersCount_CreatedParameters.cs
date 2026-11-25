using System;
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
    public class OrderApiServiceTests_GetOrdersCount_CreatedParameters
    {
        private IOrderApiService _orderApiService;
        private List<Order> _existigOrders;
        private DateTime _baseDate;

        [SetUp]
        public void Setup()
        {
            _baseDate = new DateTime(2016, 2, 23);

            _existigOrders = new List<Order>()
            {
                new Order() {Id = 2, CreatedOnUtc = _baseDate.AddMonths(2) },
                new Order() {Id = 3, CreatedOnUtc = _baseDate.AddMonths(10) },
                new Order() {Id = 1, CreatedOnUtc = _baseDate.AddMonths(7) },
                new Order() {Id = 4, CreatedOnUtc = _baseDate },
                new Order() {Id = 5, CreatedOnUtc = _baseDate.AddMonths(3) },
                new Order() {Id = 6, Deleted = true, CreatedOnUtc = _baseDate.AddMonths(10) },
                new Order() {Id = 7, CreatedOnUtc = _baseDate.AddMonths(4) }
            };

            var orderRepo = Mock.Of<IRepository<Order>>();
            Mock.Get(orderRepo).Setup(x => x.Table).Returns(_existigOrders.AsQueryable());
            
            _orderApiService = new OrderApiService(orderRepo);
        }

        [Test]
        public async Task WhenCalledWithCreatedAtMinParameter_GivenSomeOrdersCreatedAfterThatDate_ShouldReturnTheirCount()
        {
            // Arange
            DateTime createdAtMinDate = _baseDate.AddMonths(5);
            var expectedCollection =
                _existigOrders.Where(x => x.CreatedOnUtc > createdAtMinDate && !x.Deleted);
            var expectedOrdersCount = expectedCollection.Count();

            // Act
            var ordersCount = await _orderApiService.GetOrdersCountAsync(createdAtMin: createdAtMinDate);

            // Assert
            Assert.AreEqual(expectedOrdersCount, ordersCount);
        }

        [Test]
        public async Task WhenCalledWithCreatedAtMinParameter_GivenAllOrdersCreatedBeforeThatDate_ShouldReturnZero()
        {
            // Arange
            DateTime createdAtMinDate = _baseDate.AddMonths(11);

            // Act
            var ordersCount = await _orderApiService.GetOrdersCountAsync(createdAtMin: createdAtMinDate);

            // Assert
            Assert.AreEqual(0, ordersCount);
        }

        [Test]
        public async Task WhenCalledWithCreatedAtMaxParameter_GivenSomeOrdersCreatedBeforeThatDate_ShouldReturnTheirCount()
        {
            // Arange
            DateTime createdAtMaxDate = _baseDate.AddMonths(5);
            var expectedCollection = _existigOrders.Where(x => x.CreatedOnUtc < createdAtMaxDate && !x.Deleted);
            var expectedOrdersCount = expectedCollection.Count();

            // Act
            var ordersCount = await _orderApiService.GetOrdersCountAsync(createdAtMax: createdAtMaxDate);

            // Assert
            Assert.AreEqual(expectedOrdersCount, ordersCount);
        }

        [Test]
        public async Task WhenCalledWithCreatedAtMaxParameter_GivenAllOrdersCreatedAfterThatDate_ShouldReturnZero()
        {
            // Arange
            DateTime createdAtMaxDate = _baseDate.Subtract(new TimeSpan(365)); // subtract one year

            // Act
            var ordersCount = await _orderApiService.GetOrdersCountAsync(createdAtMax: createdAtMaxDate);

            // Assert
            Assert.AreEqual(0, ordersCount);
        }
    }
}