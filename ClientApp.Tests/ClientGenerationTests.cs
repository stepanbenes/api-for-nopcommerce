using System.Globalization;
using System.Net;
using System.Text.Json;
using ApiBindings;
using ApiBindings.NopApi;
using ApiBindings.NopApi.DTOs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace ClientApp.Tests;

public class ClientGenerationTests
{
    [Test]
    public void GeneratedClientExposesEveryOperation()
    {
        using var spec = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "nopapi.swagger.json")));
        var httpMethods = new HashSet<string> { "get", "post", "put", "patch", "delete", "options", "head", "trace" };
        var operations = spec.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Where(member => httpMethods.Contains(member.Name))
            .Select(member => member.Value.GetProperty("operationId").GetString())
            .ToList();

        var generatedOperations = typeof(INopApiClient).GetMethods()
            .Where(method => typeof(Task).IsAssignableFrom(method.ReturnType))
            .Select(method => method.Name);

        Assert.That(operations, Is.Not.Empty);
        Assert.That(operations, Is.Unique);
        Assert.That(generatedOperations, Is.EquivalentTo(operations));
        Assert.That(typeof(INopApiClient).IsAssignableFrom(typeof(NopApiClient)), Is.True);
    }

    [Test]
    public async Task NullableEnumQuerySendsOnlyTheSuppliedEnum()
    {
        string? query = null;
        using var http = new HttpClient(new CaptureQuery(uri => query = uri.Query))
        {
            BaseAddress = new Uri("http://127.0.0.1")
        };
        var client = new NopApiClient(http);

        await client.GetOrders(Status: OrderStatus.Pending);
        Assert.That(query, Is.EqualTo("?Status=Pending"));
        await client.GetOrders();
        Assert.That(query, Is.EqualTo(string.Empty));
    }

    [TestCase(HttpStatusCode.NoContent)]
    [TestCase(HttpStatusCode.Unauthorized)]
    public async Task ClearCacheHandlesEmptyResponsesOverHttp(HttpStatusCode status)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var server = builder.Build();

        var cacheRequest = new TaskCompletionSource<(string Body, string Authorization)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        server.MapPost("/api/admin/cache/clear", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(request.Body);
            cacheRequest.SetResult((await reader.ReadToEndAsync(cancellationToken), request.Headers.Authorization.ToString()));
            return Results.StatusCode((int)status);
        });
        await server.StartAsync();

        using var http = new HttpClient { BaseAddress = new Uri(server.Urls.Single()), Timeout = TimeSpan.FromSeconds(10) };
        var client = new NopApiClient(http);
        client.SetAuthorizationHeader("Bearer", "mock-token");

        if (status == HttpStatusCode.NoContent)
            await client.ClearCache();
        else
            Assert.That(Assert.ThrowsAsync<ApiException>(async () => await client.ClearCache())?.StatusCode, Is.EqualTo(status));

        var request = await cacheRequest.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(request.Body, Is.Empty);
        Assert.That(request.Authorization, Is.EqualTo("Bearer mock-token"));
    }

    [TestCase("en-US")]
    [TestCase("cs-CZ")]
    public async Task DateQueriesUseInvariantRoundTripFormatAndEscapeOnce(string culture)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Uri? request = null;
            using var http = new HttpClient(new CaptureQuery(uri => request = uri))
            {
                BaseAddress = new Uri("http://127.0.0.1")
            };
            var client = new NopApiClient(http);
            var utc = new DateTime(2025, 1, 23, 14, 5, 6, DateTimeKind.Utc).AddTicks(1234567);
            var withoutOffset = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);
            var local = utc.ToLocalTime();

            await client.GetOrders(CreatedAtMin: utc, CreatedAtMax: local);
            Assert.That(request!.AbsolutePath, Is.EqualTo("/api/orders"));
            var expectedLocal = Uri.EscapeDataString(local.ToString("O", CultureInfo.InvariantCulture));
            Assert.That(request.Query, Is.EqualTo($"?CreatedAtMin=2025-01-23T14%3A05%3A06.1234567Z&CreatedAtMax={expectedLocal}"));
            Assert.That(Uri.UnescapeDataString(request.Query), Does.EndWith(local.ToString("O", CultureInfo.InvariantCulture)));

            await client.GetCustomers(CreatedAtMin: withoutOffset);
            Assert.That(request!.AbsolutePath, Is.EqualTo("/api/customers"));
            Assert.That(request.Query, Is.EqualTo("?CreatedAtMin=2025-01-23T14%3A05%3A06.1234567"));

            await client.GetCustomers();
            Assert.That(request!.Query, Is.Empty);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private sealed class CaptureQuery(Action<Uri> record) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            record(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"orders":[]}""", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SendsAndReceivesCheckoutOverHttp(bool compactReceipt)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var server = builder.Build();

        var tokenRequest = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var orderRequest = new TaskCompletionSource<(string Body, string Authorization, string IdempotencyKey, string? ContentType)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        server.MapPost("/token", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(request.Body);
            tokenRequest.SetResult(await reader.ReadToEndAsync(cancellationToken));
            return Results.Text("""{"access_token":"mock-token","token_type":"Bearer"}""", "application/json");
        });
        server.MapPost("/api/orders", async (HttpRequest request, HttpResponse httpResponse, CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(request.Body);
            orderRequest.SetResult((await reader.ReadToEndAsync(cancellationToken), request.Headers.Authorization.ToString(),
                request.Headers["Idempotency-Key"].ToString(), request.ContentType));
            if (compactReceipt)
                httpResponse.Headers["Idempotency-Replayed"] = "true";
            else
                httpResponse.Headers["X-Order-Follow-Up"] = "Order was placed. Confirm payment status.";

            var response = compactReceipt
                ? """{"orders":[{"id":42,"shipping_status":null}]}"""
                : """{"orders":[{"id":42,"billing_address":{"id":7},"shipping_method":"Ground","shipping_status":"NotYetShipped"}]}""";
            return Results.Text(response, "application/json");
        });
        await server.StartAsync();

        using var http = new HttpClient { BaseAddress = new Uri(server.Urls.Single()), Timeout = TimeSpan.FromSeconds(10) };
        var client = new NopApiClient(http);
        var token = await client.RequestToken(new TokenRequest { Username = "test@example.test", Password = "test-password" });
        Assert.That(token?.AccessToken, Is.EqualTo("mock-token"));
        client.SetAuthorizationHeader(token!.TokenType!, token.AccessToken);

        var checkout = new CreateOrderRequest(new CreateOrderDto(CustomerId: 5, BillingAddress: new OrderBillingAddressDto(7))
        {
            CheckoutAttributes = [new CheckoutAttributeSelectionDto { Id = 12, Values = ["13", "14"] }]
        });
        var key = Guid.NewGuid().ToString("D");
        var result = await client.CreateOrderWithResponse(checkout, IdempotencyKey: key);

        using var tokenBody = JsonDocument.Parse(await tokenRequest.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        var request = await orderRequest.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var body = JsonDocument.Parse(request.Body);
        var order = body.RootElement.GetProperty("order");
        var selection = order.GetProperty("checkout_attributes").EnumerateArray().Single();
        Assert.Multiple(() =>
        {
            Assert.That(tokenBody.RootElement.GetProperty("username").GetString(), Is.EqualTo("test@example.test"));
            Assert.That(request.Authorization, Is.EqualTo("Bearer mock-token"));
            Assert.That(request.IdempotencyKey, Is.EqualTo(key));
            Assert.That(request.ContentType, Does.StartWith("application/json"));
            Assert.That(order.GetProperty("customer_id").GetInt32(), Is.EqualTo(5));
            Assert.That(order.GetProperty("billing_address").GetProperty("id").GetInt32(), Is.EqualTo(7));
            Assert.That(selection.GetProperty("id").GetInt32(), Is.EqualTo(12));
            Assert.That(selection.GetProperty("values").EnumerateArray().Select(value => value.GetString()), Is.EqualTo(new[] { "13", "14" }));
            Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(result.Body?.Orders?.Single().Id, Is.EqualTo(42));
            Assert.That(result.Body?.Orders?.Single().ShippingMethod, Is.EqualTo(compactReceipt ? null : "Ground"));
            Assert.That(result.Body?.Orders?.Single().ShippingStatus,
                Is.EqualTo(compactReceipt ? null : (ShippingStatus?)ShippingStatus.NotYetShipped));
            Assert.That(result.Headers.Keys, Is.EqualTo(new[] { compactReceipt ? "Idempotency-Replayed" : "X-Order-Follow-Up" }));
            Assert.That(result.Headers.Values.Single(), Is.EqualTo(new[] { compactReceipt ? "true" : "Order was placed. Confirm payment status." }));
        });
    }
}
