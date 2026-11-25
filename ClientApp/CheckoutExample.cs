using System;
using System.Linq;
using System.Threading.Tasks;
using ApiBindings.NopApi;
using ApiBindings.NopApi.DTOs;

namespace ClientApp;

internal sealed record CheckoutExample(int BillingAddressId, int ShippingAddressId, Guid Key, bool QuoteCart,
    CheckoutAttributeSelectionDto? Selection)
{
    private const string SampleShippingMethod = "Ground";
    private const string SampleShippingProvider = "Shipping.FixedByWeightByTotal";
    private const string SamplePaymentMethod = "Payments.CheckMoneyOrder";

    public static CheckoutExample? Parse(string[] args)
    {
        if (args is not [_, var mode, var billing, var shipping, var key, ..] ||
            mode is not ("checkout" or "checkout-retry") || args.Length > 6 ||
            !int.TryParse(billing, out var billingId) || billingId <= 0 ||
            !int.TryParse(shipping, out var shippingId) || shippingId <= 0 ||
            !Guid.TryParse(key, out var idempotencyKey) || idempotencyKey == Guid.Empty)
            return null;

        CheckoutAttributeSelectionDto? selection = null;
        if (args.Length == 6)
        {
            if (args[5].Split('=', 2) is not [var attribute, var value] ||
                !int.TryParse(attribute, out var attributeId) || attributeId <= 0 ||
                string.IsNullOrWhiteSpace(value))
                return null;

            selection = new CheckoutAttributeSelectionDto { Id = attributeId, Values = [value] };
        }

        return new CheckoutExample(billingId, shippingId, idempotencyKey, mode == "checkout", selection);
    }

    public async Task<int> PlaceAsync(NopApiClient client, int customerId)
    {
        var shippingMethod = SampleShippingMethod;
        var shippingProvider = SampleShippingProvider;
        var paymentMethod = SamplePaymentMethod;
        if (QuoteCart)
        {
            var shipping = await client.GetCheckoutShippingOptions(ShippingAddressId);
            var payments = await client.GetCheckoutPaymentMethods(billing_address_id: BillingAddressId);
            var ground = shipping?.ShippingOptions?.FirstOrDefault(option =>
                option.Name == SampleShippingMethod && option.ShippingRateComputationMethodSystemName == SampleShippingProvider);
            var check = payments?.PaymentMethods?.FirstOrDefault(option => option.SystemName == SamplePaymentMethod);
            if (ground is null || check is null)
            {
                Console.Error.WriteLine("This sample requires Ground shipping and Check/Money Order for the current cart and addresses.");
                return 1;
            }

            shippingMethod = ground.Name!;
            shippingProvider = ground.ShippingRateComputationMethodSystemName!;
            paymentMethod = check.SystemName!;
        }

        var request = new CreateOrderRequest(new CreateOrderDto(customerId, new OrderBillingAddressDto(BillingAddressId))
        {
            ShippingAddress = new AddressDto { Id = ShippingAddressId },
            PaymentMethodSystemName = paymentMethod,
            ShippingRateComputationMethodSystemName = shippingProvider,
            ShippingMethod = shippingMethod,
            CheckoutAttributes = Selection is null ? null : [Selection]
        });
        var response = await client.CreateOrderWithResponse(request, IdempotencyKey: Key.ToString("D"));
        if (response.Body?.Orders is not [{ Id: > 0 } order])
        {
            Console.Error.WriteLine($"The API returned no order ID. Look up the order or retry with the same key {Key:D}.");
            return 1;
        }

        Console.WriteLine($"Order ID: {order.Id}");
        Console.WriteLine($"Payment status: {order.PaymentStatus?.ToString() ?? "see order details"}");
        Console.WriteLine($"Shipping status: {order.ShippingStatus?.ToString() ?? "see order details"}");
        if (response.Headers.TryGetValue("Idempotency-Replayed", out var replayed))
            Console.WriteLine($"Replayed: {string.Join(", ", replayed)}");
        if (response.Headers.TryGetValue("X-Order-Follow-Up", out var followUp))
            Console.WriteLine(string.Join(" ", followUp));
        return 0;
    }
}
