#nullable enable

using System.Net;
using System.Text;
using ApiBindings.NopApi;
using ApiBindings.NopApi.DTOs;
using NUnit.Framework;
using TestAssert = NUnit.Framework.Assert;

namespace Nop.Plugin.Api.Tests;

public class GeneratedClientTests
{
    [TestCase(HttpStatusCode.Created)]
    [TestCase(HttpStatusCode.OK)]
    public async Task AddMenuItemDeserializesBothSuccessfulResponses(HttpStatusCode status)
    {
        using var httpClient = new HttpClient(new MenuResponseHandler(status))
        {
            BaseAddress = new Uri("https://example.test")
        };
        var client = new NopApiClient(httpClient);

        var response = await client.AddMenuItem(new AddMenuItemRequest("category") { EntityId = 42 }, 1);

        TestAssert.That(response?.MenuItems?.Single().Id, Is.EqualTo(42));
    }

    [Test]
    public async Task CreateOrderSendsIdempotencyKeyOnlyForTheRequestThatSuppliesIt()
    {
        var handler = new OrderResponseHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        var client = new NopApiClient(httpClient);
        var request = new OrderDtoDelta(new OrderDto(new AddressDto()));
        var key = Guid.NewGuid().ToString("D");

        await client.CreateOrder(request, IdempotencyKey: key);
        await client.CreateOrder(request);
        await client.CreateOrder(request, IdempotencyKey: string.Empty);

        TestAssert.That(handler.IdempotencyKeys, Is.EqualTo(new[] { key, null, string.Empty }));
    }

    private sealed class MenuResponseHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("""{"menu_items":[{"id":42}]}""", Encoding.UTF8, "application/json")
            });
    }

    private sealed class OrderResponseHandler : HttpMessageHandler
    {
        public List<string?> IdempotencyKeys { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            IdempotencyKeys.Add(request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"orders":[]}""", Encoding.UTF8, "application/json")
            });
        }
    }
}
