using Newtonsoft.Json;

namespace Nop.Plugin.Api.DTOs.Checkout;

public record ShippingOptionsDto(
    [property: JsonProperty("shipping_options")] IReadOnlyList<ShippingQuoteDto> ShippingOptions);

public record ShippingQuoteDto(
    [property: JsonProperty("name")] string Name,
    [property: JsonProperty("shipping_rate_computation_method_system_name")] string ShippingRateComputationMethodSystemName,
    [property: JsonProperty("rate")] decimal Rate);

public record PaymentMethodsDto(
    [property: JsonProperty("payment_methods")] IReadOnlyList<CheckoutPaymentMethodDto> PaymentMethods);

public record CheckoutPaymentMethodDto(
    [property: JsonProperty("name")] string Name,
    [property: JsonProperty("system_name")] string SystemName);
