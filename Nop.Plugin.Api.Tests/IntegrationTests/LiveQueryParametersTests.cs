using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NUnit.Framework;
using TestAssert = NUnit.Framework.Assert;

namespace Nop.Plugin.Api.Tests.IntegrationTests;

[TestFixture]
[Category("Integration")]
public class LiveQueryParametersTests
{
    private HttpClient _client;

    [SetUp]
    public async Task SetUp()
    {
        var url = Environment.GetEnvironmentVariable("NOP_API_TEST_URL");
        if (string.IsNullOrWhiteSpace(url))
            TestAssert.Ignore("Set NOP_API_TEST_URL to an installed test store to run the live API tests.");

        _client = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(url)
        };

        var token = await Send(HttpMethod.Post, "/token", HttpStatusCode.OK, new
        {
            username = Environment.GetEnvironmentVariable("NOP_API_TEST_USER"),
            password = Environment.GetEnvironmentVariable("NOP_API_TEST_PASSWORD")
        });
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.GetProperty("access_token").GetString());
    }

    [TearDown]
    public void TearDown() => _client?.Dispose();

    [TestCase("/api/customers", "customers")]
    [TestCase("/api/orders", "orders")]
    public async Task DateAliasesFilterResultsAndBadValuesReturnStructuredErrors(string path, string collection)
    {
        var offset = await Send(HttpMethod.Get, $"{path}?created_at_min=2099-01-01T04:30:00%2B01:30",
            HttpStatusCode.OK);
        TestAssert.That(offset.GetProperty(collection).GetArrayLength(), Is.Zero);

        var legacy = await Send(HttpMethod.Get, $"{path}?CreatedAtMin=2099-01-01%2003:00",
            HttpStatusCode.OK);
        TestAssert.That(legacy.GetProperty(collection).GetArrayLength(), Is.Zero);

        var invalid = await Send(HttpMethod.Get, $"{path}?CreatedAtMin=invalid", HttpStatusCode.BadRequest);
        TestAssert.That(invalid.GetProperty("errors").GetProperty("created_at_min").GetArrayLength(), Is.Positive);
    }

    [Test]
    public async Task TopicsKeepDefaultPagingAndCanExplicitlyReturnAllMatchingResults()
    {
        var topics = new List<int>();
        try
        {
            for (var index = 0; index < 51; index++)
            {
                var name = $"Paging integration {Guid.NewGuid():N}";
                var response = await Send(HttpMethod.Post, "/api/topics", HttpStatusCode.OK,
                    new { topic = new { system_name = name, title = name, body = name } });
                var id = response.GetProperty("topics")[0].GetProperty("id").GetInt32();
                topics.Add(id);
                await Send(HttpMethod.Put, $"/api/topics/{id}", HttpStatusCode.OK, new { topic = new { published = true } });
            }

            var query = $"SinceId={topics.Min() - 1}";
            var paged = await Send(HttpMethod.Get, $"/api/topics?{query}", HttpStatusCode.OK);
            var unpaged = await Send(HttpMethod.Get, $"/api/topics?{query}&unpaged=true", HttpStatusCode.OK);
            TestAssert.That(paged.GetProperty("topics").GetArrayLength(), Is.EqualTo(50));
            var matchingIds = unpaged.GetProperty("topics").EnumerateArray().Select(topic => topic.GetProperty("id").GetInt32());
            TestAssert.That(matchingIds, Is.SupersetOf(topics));

            var conflict = await Send(HttpMethod.Get, "/api/topics?unpaged=true&page=1", HttpStatusCode.BadRequest);
            TestAssert.That(conflict.GetProperty("errors").GetProperty("unpaged").GetArrayLength(), Is.Positive);
        }
        finally
        {
            foreach (var topic in topics)
                await Send(HttpMethod.Delete, $"/api/topics/{topic}", HttpStatusCode.OK);
        }
    }

    [Test]
    public async Task CustomerSearchOrdersMatchingRowsBeforePaging()
    {
        var roles = await Send(HttpMethod.Get, "/api/customer_roles", HttpStatusCode.OK);
        var registered = roles.GetProperty("customer_roles").EnumerateArray()
            .Single(role => role.GetProperty("system_name").GetString() == "Registered").GetProperty("id").GetInt32();
        var prefix = $"paging-{Guid.NewGuid():N}";
        var customers = new List<int>();
        try
        {
            foreach (var suffix in new[] { "a", "b" })
            {
                var created = await Send(HttpMethod.Post, "/api/customers", HttpStatusCode.OK, new
                {
                    customer = new
                    {
                        email = $"{prefix}-{suffix}@example.test",
                        first_name = "Tied",
                        password = Guid.NewGuid().ToString("N"),
                        role_ids = new[] { registered }
                    }
                });
                customers.Add(created.GetProperty("customers")[0].GetProperty("id").GetInt32());
            }

            var search = $"/api/customers/search?query={Uri.EscapeDataString($"email:{prefix}")}&limit=1";
            var newest = await Send(HttpMethod.Get, $"{search}&order=Id%20desc&page=1", HttpStatusCode.OK);
            var next = await Send(HttpMethod.Get, $"{search}&order=Id%20desc&page=2", HttpStatusCode.OK);
            var tied = await Send(HttpMethod.Get, $"{search}&order=first_name%20asc&page=1", HttpStatusCode.OK);
            TestAssert.That(newest.GetProperty("customers")[0].GetProperty("id").GetInt32(), Is.EqualTo(customers[1]));
            TestAssert.That(next.GetProperty("customers")[0].GetProperty("id").GetInt32(), Is.EqualTo(customers[0]));
            TestAssert.That(tied.GetProperty("customers")[0].GetProperty("id").GetInt32(), Is.EqualTo(customers[0]));
        }
        finally
        {
            foreach (var id in customers)
                await Send(HttpMethod.Delete, $"/api/customers/{id}", HttpStatusCode.OK);
        }
    }

    private async Task<JsonElement> Send(HttpMethod method, string path, HttpStatusCode status, object body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        using var response = await _client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        TestAssert.That(response.StatusCode, Is.EqualTo(status), path == "/token" ? path : $"{method} {path} {json}");
        if (string.IsNullOrWhiteSpace(json))
            return default;

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
