using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Shipping;
using Nop.Plugin.Api.Infrastructure;

namespace Nop.Plugin.Api.Services
{
    public interface IOrderApiService
    {
        Task<IList<Order>> GetOrdersByCustomerIdAsync(int customerId, int? storeId = null);

        Task<IList<Order>> GetOrdersAsync(
            IList<int> ids = null, DateTime? createdAtMin = null, DateTime? createdAtMax = null,
            int limit = Constants.Configurations.DefaultLimit, int page = Constants.Configurations.DefaultPageValue,
            int sinceId = Constants.Configurations.DefaultSinceId, OrderStatus? status = null, PaymentStatus? paymentStatus = null,
            ShippingStatus? shippingStatus = null, int? customerId = null, int? storeId = null);

        Task<Order> GetOrderByIdAsync(int orderId, int? storeId = null);

        Task<int> GetOrdersCountAsync(
            DateTime? createdAtMin = null, DateTime? createdAtMax = null, OrderStatus? status = null,
            PaymentStatus? paymentStatus = null, ShippingStatus? shippingStatus = null,
            int? customerId = null, int? storeId = null);
    }
}
