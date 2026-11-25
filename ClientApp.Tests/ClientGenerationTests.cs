using System.Net;
using System.Text.Json;
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
                ? """{"orders":[{"id":42}]}"""
                : """{"orders":[{"id":42,"billing_address":{"id":7},"shipping_method":"Ground"}]}""";
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
            Assert.That(result.Headers.Keys, Is.EqualTo(new[] { compactReceipt ? "Idempotency-Replayed" : "X-Order-Follow-Up" }));
            Assert.That(result.Headers.Values.Single(), Is.EqualTo(new[] { compactReceipt ? "true" : "Order was placed. Confirm payment status." }));
        });
    }
}
