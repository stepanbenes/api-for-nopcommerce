using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NUnit.Framework;
using TestAssert = NUnit.Framework.Assert;

namespace Nop.Plugin.Api.Tests.IntegrationTests;

[TestFixture]
[Category("Integration")]
public class LiveCustomerWriteTests
{
    private HttpClient _client;
    private int _registeredRoleId;
    private int _countryId;

    [SetUp]
    public async Task SetUp()
    {
        var baseUrl = Environment.GetEnvironmentVariable("NOP_API_TEST_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
            TestAssert.Ignore("Set NOP_API_TEST_URL to an installed test store to run the live API tests.");

        _client = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(baseUrl)
        };

        var token = await Send(HttpMethod.Post, "/api/token", HttpStatusCode.OK, new
        {
            username = Environment.GetEnvironmentVariable("NOP_API_TEST_USER"),
            password = Environment.GetEnvironmentVariable("NOP_API_TEST_PASSWORD")
        });
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("access_token").GetString());

        var roles = (await Send(HttpMethod.Get, "/api/customer_roles", HttpStatusCode.OK)).GetProperty("customer_roles");
        _registeredRoleId = roles.EnumerateArray().Single(role => role.GetProperty("system_name").GetString() == "Registered")
            .GetProperty("id").GetInt32();
        var countries = (await Send(HttpMethod.Get, "/api/countries", HttpStatusCode.OK)).GetProperty("countries");
        _countryId = countries[0].GetProperty("id").GetInt32();
    }

    [TearDown]
    public void TearDown() => _client?.Dispose();

    [Test]
    public async Task InlineAddressCreateAndPartialUpdatePersistAndUnknownRoleDoesNotChangeCustomer()
    {
        var email = NewEmail();
        var customer = (await Send(HttpMethod.Post, "/api/customers", HttpStatusCode.OK, new
        {
            customer = new
            {
                email,
                first_name = "Initial",
                password = Guid.NewGuid().ToString("N"),
                role_ids = new[] { _registeredRoleId },
                addresses = new[] { Address(email, _countryId) }
            }
        })).GetProperty("customers")[0];

        var path = $"/api/customers/{customer.GetProperty("id").GetInt32()}";
        try
        {
            var addressId = customer.GetProperty("addresses")[0].GetProperty("id").GetInt32();
            TestAssert.That(addressId, Is.Positive);

            await Send(HttpMethod.Put, path, HttpStatusCode.OK, new
            {
                customer = new { addresses = new[] { new { id = addressId, city = "Brno" } } }
            });
            await Send(HttpMethod.Put, path, HttpStatusCode.UnprocessableEntity, new
            {
                customer = new { first_name = "Rejected", role_ids = new[] { _registeredRoleId, int.MaxValue } }
            });

            var persisted = (await Send(HttpMethod.Get, path, HttpStatusCode.OK)).GetProperty("customers")[0];
            TestAssert.That(persisted.GetProperty("first_name").GetString(), Is.EqualTo("Initial"));
            TestAssert.That(persisted.GetProperty("role_ids").EnumerateArray().Select(role => role.GetInt32()), Is.EqualTo(new[] { _registeredRoleId }));
            var address = persisted.GetProperty("addresses")[0];
            TestAssert.That(address.GetProperty("id").GetInt32(), Is.EqualTo(addressId));
            TestAssert.That(address.GetProperty("city").GetString(), Is.EqualTo("Brno"));
            TestAssert.That(address.GetProperty("address1").GetString(), Is.EqualTo("1 Integration Road"));
        }
        finally
        {
            await Send(HttpMethod.Delete, path, HttpStatusCode.OK);
        }
    }

    [Test]
    public async Task RejectedAddressInsertRollsBackCustomerAndEarlierAddressMapping()
    {
        var email = NewEmail();
        var customer = (await Send(HttpMethod.Post, "/api/customers", HttpStatusCode.OK, new
        {
            customer = new { email, first_name = "Initial", password = Guid.NewGuid().ToString("N"), role_ids = new[] { _registeredRoleId } }
        })).GetProperty("customers")[0];

        var path = $"/api/customers/{customer.GetProperty("id").GetInt32()}";
        try
        {
            await Send(HttpMethod.Put, path, HttpStatusCode.InternalServerError, new
            {
                customer = new { first_name = "Rejected", addresses = new[] { Address(email, _countryId), Address(email, int.MaxValue) } }
            });

            var persisted = (await Send(HttpMethod.Get, path, HttpStatusCode.OK)).GetProperty("customers")[0];
            TestAssert.That(persisted.GetProperty("first_name").GetString(), Is.EqualTo("Initial"));
            TestAssert.That(persisted.GetProperty("addresses").GetArrayLength(), Is.Zero);
        }
        finally
        {
            await Send(HttpMethod.Delete, path, HttpStatusCode.OK);
        }
    }

    private static string NewEmail() => $"customer-integration-{Guid.NewGuid():N}@example.test";

    private static object Address(string email, int countryId) => new
    {
        email,
        first_name = "Integration",
        last_name = "Customer",
        country_id = countryId,
        address1 = "1 Integration Road",
        city = "Prague",
        zip_postal_code = "11000",
        phone_number = "5550100"
    };

    private async Task<JsonElement> Send(HttpMethod method, string path, HttpStatusCode status, object body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        using var response = await _client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        TestAssert.That(response.StatusCode, Is.EqualTo(status), $"{method} {path}: {json}");
        return string.IsNullOrWhiteSpace(json) ? default : JsonDocument.Parse(json).RootElement.Clone();
    }
}
