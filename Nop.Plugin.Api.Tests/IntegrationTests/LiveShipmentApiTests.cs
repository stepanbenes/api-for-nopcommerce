using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NUnit.Framework;
using TestAssert = NUnit.Framework.Assert;

namespace Nop.Plugin.Api.Tests.IntegrationTests;

public partial class LiveApiTests
{
    private async Task VerifyShipmentLifecycleAsync(int orderId, int orderItemId, PostgresProbe database)
    {
        var path = $"/api/orders/{orderId}/shipments";
        await Send(HttpMethod.Get, "/api/orders/2147483647/shipments", HttpStatusCode.NotFound);
        await Send(HttpMethod.Post, path, HttpStatusCode.BadRequest, new
        {
            items = new[] { new { order_item_id = orderItemId, quantity = 1, warehouse_id = int.MaxValue } }
        });

        var first = await Send(HttpMethod.Post, path, HttpStatusCode.Created, new
        {
            tracking_number = "initial",
            items = new[] { new { order_item_id = orderItemId, quantity = 1 } }
        });
        var firstPath = $"{path}/{first.GetProperty("id").GetInt32()}";
        await Send(HttpMethod.Post, $"{firstPath}/deliver", HttpStatusCode.Conflict);
        await Send(HttpMethod.Post, $"{firstPath}/ready-for-pickup", HttpStatusCode.Conflict);

        var updated = await Send(HttpMethod.Put, firstPath, HttpStatusCode.OK,
            new { tracking_number = "final tracking", admin_comment = "ready to send" });
        TestAssert.That(updated.GetProperty("tracking_number").GetString(), Is.EqualTo("final tracking"));
        TestAssert.That(updated.GetProperty("admin_comment").GetString(), Is.EqualTo("ready to send"));

        async Task<(HttpStatusCode Status, string Body)> AttemptFinalShipment()
        {
            using var response = await _client.PostAsJsonAsync(path, new
            {
                items = new[] { new { order_item_id = orderItemId, quantity = 2 } }
            });
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        var attempts = await Task.WhenAll(AttemptFinalShipment(), AttemptFinalShipment());
        TestAssert.That(attempts.Select(attempt => attempt.Status),
            Is.EquivalentTo(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }),
            string.Join("\n", attempts.Select(attempt => $"{attempt.Status}: {attempt.Body}")));

        using var finalShipment = JsonDocument.Parse(attempts.Single(attempt => attempt.Status == HttpStatusCode.Created).Body);
        var finalId = finalShipment.RootElement.GetProperty("id").GetInt32();
        var finalPath = $"{path}/{finalId}";
        await Send(HttpMethod.Get, finalPath, HttpStatusCode.OK);
        var all = await Send(HttpMethod.Get, path, HttpStatusCode.OK);
        TestAssert.That(all.GetProperty("shipments").GetArrayLength(), Is.EqualTo(2));

        var shipped = await Send(HttpMethod.Post, $"{firstPath}/ship", HttpStatusCode.OK);
        TestAssert.That(shipped.GetProperty("shipped_date_utc").ValueKind, Is.EqualTo(JsonValueKind.String));
        var order = await Send(HttpMethod.Get, $"/api/orders/{orderId}", HttpStatusCode.OK);
        TestAssert.That(order.GetProperty("orders")[0].GetProperty("shipping_status").GetString(), Is.EqualTo("PartiallyShipped"));
        await Send(HttpMethod.Post, $"{firstPath}/ship", HttpStatusCode.Conflict);
        await Send(HttpMethod.Post, $"{finalPath}/ship", HttpStatusCode.OK);
        await Send(HttpMethod.Post, $"{firstPath}/deliver", HttpStatusCode.OK);
        var delivered = await Send(HttpMethod.Post, $"{finalPath}/deliver", HttpStatusCode.OK);
        TestAssert.That(delivered.GetProperty("delivery_date_utc").ValueKind, Is.EqualTo(JsonValueKind.String));
        await Send(HttpMethod.Post, $"{finalPath}/deliver", HttpStatusCode.Conflict);
        order = await Send(HttpMethod.Get, $"/api/orders/{orderId}", HttpStatusCode.OK);
        TestAssert.That(order.GetProperty("orders")[0].GetProperty("shipping_status").GetString(), Is.EqualTo("Delivered"));

        if (database is not null)
        {
            const string shipmentQuery = """
                SELECT jsonb_build_object('count', count(*), 'quantity', sum(i."Quantity"),
                    'delivered', bool_and(s."DeliveryDateUtc" IS NOT NULL))
                FROM "Shipment" s JOIN "ShipmentItem" i ON i."ShipmentId" = s."Id"
                WHERE s."OrderId" = :'id'::integer
                """;
            var rows = await database.ReadAsync(shipmentQuery, orderId);
            TestAssert.That(rows.GetProperty("count").GetInt32(), Is.EqualTo(2));
            TestAssert.That(rows.GetProperty("quantity").GetInt32(), Is.EqualTo(3));
            TestAssert.That(rows.GetProperty("delivered").GetBoolean(), Is.True);
        }
    }
}
