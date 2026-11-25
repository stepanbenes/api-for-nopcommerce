using System.Collections.Generic;
using System.Linq;
using Nop.Data;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Shipping;
using Nop.Plugin.Api.Services;
using NUnit.Framework;
using Moq;

namespace Nop.Plugin.Api.Tests.ServicesTests.Orders.GetOrders
{
    [TestFixture]
    public class OrderApiServiceTests_GetOrders_StatusesParameters
    {
        private IOrderApiService _orderApiService;
        private List<Order> _existigOrders;

        [SetUp]
        public void Setup()
        {
            _existigOrders = new List<Order>()
            {
                new Order() {Id = 2, OrderStatus = OrderStatus.Complete },
                new Order() {Id = 3, PaymentStatus = PaymentStatus.Paid },
                new Order() {Id = 1, ShippingStatus = ShippingStatus.Delivered },
                new Order() {Id = 4, OrderStatus = OrderStatus.Cancelled, PaymentStatus = PaymentStatus.Pending },
                new Order() {Id = 5, ShippingStatus = ShippingStatus.NotYetShipped, PaymentStatus = PaymentStatus.Paid },
                new Order() {Id = 6, OrderStatus = OrderStatus.Complete, ShippingStatus = ShippingStatus.ShippingNotRequired },
                new Order() {Id = 7, OrderStatus = OrderStatus.Cancelled, PaymentStatus = PaymentStatus.Refunded, ShippingStatus = ShippingStatus.NotYetShipped }
            };

            var orderRepo = Mock.Of<IRepository<Order>>();
            Mock.Get(orderRepo).Setup(x => x.Table).Returns(_existigOrders.AsQueryable());

            _orderApiService = new OrderApiService(orderRepo);
        }

        [Test]
        [TestCase(OrderStatus.Complete)]
        [TestCase(OrderStatus.Cancelled)]
        public async Task WhenCalledWithSomeOrderStatus_GivenOrdersCollectionContainingOrdersHavingThisStatus_ShouldReturnTheOrdersHavingThisStatusOrderedById(OrderStatus orderStatus)
        {
            // Arange
            var expectedCollection = _existigOrders.Where(x => x.OrderStatus == orderStatus).OrderBy(x => x.Id);

            // Act
            var orders = await _orderApiService.GetOrdersAsync(status: orderStatus);

            // Assert
            CollectionAssert.IsNotEmpty(orders);
            Assert.AreEqual(expectedCollection.Count(), orders.Count);
            Assert.IsTrue(orders.Select(x => x.Id).SequenceEqual(expectedCollection.Select(x => x.Id)));
        }

        [Test]
        [TestCase(PaymentStatus.Paid)]
        [TestCase(PaymentStatus.Refunded)]
        public async Task WhenCalledWithSomePaymentStatus_GivenOrdersCollectionContainingOrdersHavingThisStatus_ShouldReturnTheOrdersHavingThisStatusOrderedById(PaymentStatus paymentStatus)
        {
            // Arange
            var expectedCollection = _existigOrders.Where(x => x.PaymentStatus == paymentStatus).OrderBy(x => x.Id);

            // Act
            var orders = await _orderApiService.GetOrdersAsync(paymentStatus: paymentStatus);

            // Assert
            CollectionAssert.IsNotEmpty(orders);
            Assert.AreEqual(expectedCollection.Count(), orders.Count);
            Assert.IsTrue(orders.Select(x => x.Id).SequenceEqual(expectedCollection.Select(x => x.Id)));
        }

        [Test]
        [TestCase(ShippingStatus.Delivered)]
        [TestCase(ShippingStatus.ShippingNotRequired)]
        public async Task WhenCalledWithSomeShippingStatus_GivenOrdersCollectionContainingOrdersHavingThisStatus_ShouldReturnTheOrdersHavingThisStatusOrderedById(ShippingStatus shippingStatus)
        {
            // Arange
            var expectedCollection = _existigOrders.Where(x => x.ShippingStatus == shippingStatus).OrderBy(x => x.Id);

            // Act
            var orders = await _orderApiService.GetOrdersAsync(shippingStatus: shippingStatus);

            // Assert
            CollectionAssert.IsNotEmpty(orders);
            Assert.AreEqual(expectedCollection.Count(), orders.Count);
            Assert.IsTrue(orders.Select(x => x.Id).SequenceEqual(expectedCollection.Select(x => x.Id)));
        }

        [Test]
        public async Task WhenCalledWithStatus_GivenOrdersCollectionThatDoNotHaveThisStatus_ShouldReturnEmptyCollection()
        {
            // Arange
            var orderStatus = OrderStatus.Processing;
            var paymentStatus = PaymentStatus.PartiallyRefunded;
            var shippingStatus = ShippingStatus.Shipped;

            // Act
            var orders = new List<Order>();

            List<Order> ordersByOrderStatus = (await _orderApiService.GetOrdersAsync(status: orderStatus)).ToList();
            List<Order> ordersByPaymentStatus = (await _orderApiService.GetOrdersAsync(paymentStatus: paymentStatus)).ToList();
            List<Order> ordersByShippingStatus = (await _orderApiService.GetOrdersAsync(shippingStatus: shippingStatus)).ToList();

            orders.AddRange(ordersByOrderStatus);
            orders.AddRange(ordersByPaymentStatus);
            orders.AddRange(ordersByShippingStatus);

            // Assert
            CollectionAssert.IsEmpty(orders);
        }
    }
}