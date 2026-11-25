using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NUnit.Framework;
using TestAssert = NUnit.Framework.Assert;

namespace Nop.Plugin.Api.Tests.IntegrationTests;

[TestFixture]
[Category("Integration")]
public class LiveMenuApiTests
{
    private HttpClient _client;

    [SetUp]
    public void SetUp()
    {
        var baseUrl = Environment.GetEnvironmentVariable("NOP_API_TEST_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
            TestAssert.Ignore("Set NOP_API_TEST_URL to an installed test store to run the live API tests.");

        _client = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(baseUrl)
        };
    }

    [TearDown]
    public void TearDown() => _client?.Dispose();

    [TestCase("category", "categories")]
    [TestCase("topic", "topics")]
    public async Task ExplicitLinkCanBeAddedListedRetriedAndRemoved(string linkType, string collection)
    {
        await AuthenticateAdministrator();
        var menus = (await Send(HttpMethod.Get, "/api/menus", HttpStatusCode.OK)).GetProperty("menus");
        var menu = menus.EnumerateArray().First(entry => entry.GetProperty("menu_type").GetString() == "Main");
        TestAssert.That(menu.GetProperty("name").GetString(), Is.Not.Empty);
        var menuId = menu.GetProperty("id").GetInt32();
        var path = $"/api/menus/{menuId}/items";
        var name = $"Menu integration {Guid.NewGuid():N}";
        object entity = linkType == "category"
            ? new { category = new { name, published = true } }
            : new { topic = new { system_name = name, title = name, body = name } };
        var resource = (await Send(HttpMethod.Post, $"/api/{collection}", HttpStatusCode.OK, entity))
            .GetProperty(collection)[0];
        var entityId = resource.GetProperty("id").GetInt32();
        int? itemId = null;
        try
        {
            await Send(HttpMethod.Post, path, HttpStatusCode.BadRequest, new { link_type = "product", entity_id = entityId });
            var body = new { link_type = linkType, entity_id = entityId, display_order = 312 };
            var item = (await Send(HttpMethod.Post, path, HttpStatusCode.Created, body)).GetProperty("menu_items")[0];
            itemId = item.GetProperty("id").GetInt32();
            TestAssert.That(item.GetProperty("menu_id").GetInt32(), Is.EqualTo(menuId));
            TestAssert.That(item.GetProperty("parent_id").GetInt32(), Is.Zero);
            TestAssert.That(item.GetProperty("entity_id").GetInt32(), Is.EqualTo(entityId));
            TestAssert.That(item.GetProperty("display_order").GetInt32(), Is.EqualTo(312));

            var retried = (await Send(HttpMethod.Post, path, HttpStatusCode.OK, body)).GetProperty("menu_items")[0];
            TestAssert.That(retried.GetProperty("id").GetInt32(), Is.EqualTo(itemId));
            var listed = (await Send(HttpMethod.Get, path, HttpStatusCode.OK)).GetProperty("menu_items");
            TestAssert.That(listed.EnumerateArray().Count(entry => entry.GetProperty("id").GetInt32() == itemId), Is.EqualTo(1));

            var otherMenu = menus.EnumerateArray().FirstOrDefault(entry => entry.GetProperty("id").GetInt32() != menuId);
            if (otherMenu.ValueKind != JsonValueKind.Undefined)
            {
                var otherMenuId = otherMenu.GetProperty("id").GetInt32();
                await Send(HttpMethod.Delete, $"/api/menus/{otherMenuId}/items/{itemId}", HttpStatusCode.NotFound);
            }

            var removedId = itemId.Value;
            await Send(HttpMethod.Delete, $"{path}/{removedId}", HttpStatusCode.NoContent);
            itemId = null;
            var remaining = (await Send(HttpMethod.Get, path, HttpStatusCode.OK)).GetProperty("menu_items");
            TestAssert.That(remaining.EnumerateArray().Any(entry => entry.GetProperty("id").GetInt32() == removedId), Is.False);
        }
        finally
        {
            if (itemId.HasValue)
                await Send(HttpMethod.Delete, $"{path}/{itemId}", HttpStatusCode.NoContent);

            await Send(HttpMethod.Delete, $"/api/{collection}/{entityId}", HttpStatusCode.OK);
        }
    }

    [Test]
    public async Task MenuOperationsRequireAuthenticationAndManageMenusPermission()
    {
        await Send(HttpMethod.Get, "/api/menus", HttpStatusCode.Unauthorized);
        var guest = await Authenticate(new { guest = true });
        try
        {
            await Send(HttpMethod.Get, "/api/menus", HttpStatusCode.Forbidden);
            await Send(HttpMethod.Get, "/api/menus/1/items", HttpStatusCode.Forbidden);
            await Send(HttpMethod.Post, "/api/menus/1/items", HttpStatusCode.Forbidden, new { link_type = "category", entity_id = 1 });
            await Send(HttpMethod.Delete, "/api/menus/1/items/1", HttpStatusCode.Forbidden);
        }
        finally
        {
            await AuthenticateAdministrator();
            await Send(HttpMethod.Delete, $"/api/customers/{guest}", HttpStatusCode.OK);
        }
    }

    private Task<int> AuthenticateAdministrator() => Authenticate(new
    {
        username = Environment.GetEnvironmentVariable("NOP_API_TEST_USER"),
        password = Environment.GetEnvironmentVariable("NOP_API_TEST_PASSWORD")
    });

    private async Task<int> Authenticate(object credentials)
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var token = await Send(HttpMethod.Post, "/token", HttpStatusCode.OK, credentials);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("access_token").GetString());
        return token.GetProperty("customer_id").GetInt32();
    }

    private async Task<JsonElement> Send(HttpMethod method, string path, HttpStatusCode status, object body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        using var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        TestAssert.That(response.StatusCode, Is.EqualTo(status),
            path == "/token" ? $"{method} {path}" : $"{method} {path}\n{content}");

        if (string.IsNullOrWhiteSpace(content))
            return default;

        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }
}
