using System.Net;
using NUnit.Framework;
using TestAssert = NUnit.Framework.Assert;

namespace Nop.Plugin.Api.Tests.IntegrationTests;

public partial class LiveApiTests
{
    [TestCase(false)]
    [TestCase(true, Category = "DatabaseIntegration")]
    public async Task CacheClearRequiresMaintenancePermissionAndAuditsTheCaller(bool verifyDatabase)
    {
        var database = verifyDatabase ? PostgresProbe.ForConfiguredContainer(_client.BaseAddress) : null;
        const string path = "/api/admin/cache/clear";
        const string readActivityCount = """
            SELECT to_jsonb(count(*))
            FROM "ActivityLog" AS log
            JOIN "ActivityLogType" AS type ON type."Id" = log."ActivityLogTypeId"
            WHERE type."SystemKeyword" = 'Api.ClearCache' AND log."CustomerId" = :'id'::integer
            """;

        await Send(HttpMethod.Post, path, HttpStatusCode.Unauthorized);
        await Authenticate(new { guest = true });
        var guestId = _customerId;
        try
        {
            await Send(HttpMethod.Get, "/api/products?limit=1", HttpStatusCode.OK);
            await Send(HttpMethod.Post, path, HttpStatusCode.Forbidden);
            if (database is not null)
                TestAssert.That((await database.ReadAsync(readActivityCount, guestId)).GetInt64(), Is.Zero);
        }
        finally
        {
            _client.DefaultRequestHeaders.Authorization = null;
            await AuthenticateAdministrator();
            await Send(HttpMethod.Delete, $"/api/customers/{guestId}", HttpStatusCode.OK);
        }

        var previousCount = database is null ? 0 : (await database.ReadAsync(readActivityCount, _customerId)).GetInt64();
        await Send(HttpMethod.Post, path, HttpStatusCode.NoContent);
        if (database is not null)
            TestAssert.That((await database.ReadAsync(readActivityCount, _customerId)).GetInt64(),
                Is.EqualTo(previousCount + 1));
    }
}
