using System.Net;
using System.Text.Json;
using NUnit.Framework;
using TestAssert = NUnit.Framework.Assert;

namespace Nop.Plugin.Api.Tests.IntegrationTests;

public partial class LiveApiTests
{
    [Test]
    public async Task PartialProductPutRetainsTagsPublicationAndCategories()
    {
        await AuthenticateAdministrator();
        var name = $"Partial PUT {Guid.NewGuid():N}";
        var product = (await Send(HttpMethod.Post, "/api/products", HttpStatusCode.OK, new
        {
            product = new { name, published = true, price = 17m, stock_quantity = 5, tags = new[] { name } }
        })).GetProperty("products")[0];
        var productId = product.GetProperty("id").GetInt32();
        var productPath = $"/api/products/{productId}";
        int? categoryId = null;
        int? mappingId = null;
        const string mappingPath = "/api/product_category_mappings";

        try
        {
            var category = (await Send(HttpMethod.Post, "/api/categories", HttpStatusCode.OK,
                new { category = new { name, published = true } })).GetProperty("categories")[0];
            categoryId = category.GetProperty("id").GetInt32();
            var mappingRequest = new { product_category_mapping = new { product_id = productId, category_id = categoryId, display_order = 17 } };
            var additions = await Task.WhenAll(
                Send(HttpMethod.Post, mappingPath, HttpStatusCode.OK, mappingRequest),
                Send(HttpMethod.Post, mappingPath, HttpStatusCode.OK, mappingRequest));
            mappingId = additions[0].GetProperty("product_category_mappings")[0].GetProperty("id").GetInt32();
            TestAssert.That(additions[1].GetProperty("product_category_mappings")[0].GetProperty("id").GetInt32(),
                Is.EqualTo(mappingId.Value));
            await Send(HttpMethod.Post, mappingPath, HttpStatusCode.Conflict,
                new { product_category_mapping = new { product_id = productId, category_id = categoryId, display_order = 99 } });

            var reordered = (await Send(HttpMethod.Put, $"{mappingPath}/{mappingId}", HttpStatusCode.OK,
                new { product_category_mapping = new { display_order = 23 } })).GetProperty("product_category_mappings")[0];
            TestAssert.That(reordered.GetProperty("display_order").GetInt32(), Is.EqualTo(23));

            var update = new { product = new { price = 25m, stock_quantity = 6 } };
            var updated = (await Send(HttpMethod.Put, productPath, HttpStatusCode.OK, update)).GetProperty("products")[0];
            TestAssert.That(updated.GetProperty("price").GetDecimal(), Is.EqualTo(25m));
            TestAssert.That(updated.GetProperty("stock_quantity").GetInt32(), Is.EqualTo(6));
            var stored = (await Send(HttpMethod.Get, productPath, HttpStatusCode.OK)).GetProperty("products")[0];
            var repeated = (await Send(HttpMethod.Put, productPath, HttpStatusCode.OK, update)).GetProperty("products")[0];
            TestAssert.That(repeated.GetProperty("updated_on_utc").GetString(), Is.EqualTo(stored.GetProperty("updated_on_utc").GetString()));
            TestAssert.That(repeated.GetProperty("published").GetBoolean(), Is.True);
            TestAssert.That(repeated.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()), Is.EqualTo(new[] { name }));

            var categoryMappings = (await Send(HttpMethod.Get, $"{mappingPath}?product_id={productId}&category_id={categoryId}",
                HttpStatusCode.OK)).GetProperty("product_category_mappings");
            TestAssert.That(categoryMappings.GetArrayLength(), Is.EqualTo(1));
            TestAssert.That(categoryMappings[0].GetProperty("id").GetInt32(), Is.EqualTo(mappingId.Value));
            TestAssert.That(categoryMappings[0].GetProperty("display_order").GetInt32(), Is.EqualTo(23));

            var cleared = (await Send(HttpMethod.Put, productPath, HttpStatusCode.OK,
                new { product = new { tags = Array.Empty<string>() } })).GetProperty("products")[0];
            TestAssert.That(cleared.GetProperty("tags").GetArrayLength(), Is.Zero);
            TestAssert.That(cleared.GetProperty("published").GetBoolean(), Is.True);
        }
        finally
        {
            if (mappingId.HasValue)
                await Send(HttpMethod.Delete, $"{mappingPath}/{mappingId}", HttpStatusCode.OK);
            if (categoryId.HasValue)
                await Send(HttpMethod.Delete, $"/api/categories/{categoryId}", HttpStatusCode.OK);
            await Send(HttpMethod.Delete, productPath, HttpStatusCode.OK);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ProductLinksAreReplaySafeAndCanBeReordered(bool specification)
    {
        await AuthenticateAdministrator();
        var name = $"Relationship {Guid.NewGuid():N}";
        var productId = Id((await Send(HttpMethod.Post, "/api/products", HttpStatusCode.OK,
            new { product = new { name, published = true } })).GetProperty("products")[0]);
        var path = specification ? "/api/productspecificationattributes" : "/api/product_manufacturer_mappings";
        var envelope = specification ? "product_specification_attributes" : "product_manufacturer_mappings";
        var targetPath = specification ? "/api/specificationattributes" : "/api/manufacturers";
        int? targetId = null;
        int? mappingId = null;

        try
        {
            int targetKey;
            if (specification)
            {
                targetId = Id((await Send(HttpMethod.Post, targetPath, HttpStatusCode.OK,
                    new { specification_attribute = new { name } })).GetProperty("specification_attributes")[0]);
                var attribute = (await Send(HttpMethod.Put, $"{targetPath}/{targetId}", HttpStatusCode.OK, new
                {
                    specification_attribute = new
                    {
                        specification_attribute_options = new[] { new { specification_attribute_id = targetId.Value, name } }
                    }
                })).GetProperty("specification_attributes")[0];
                targetKey = Id(attribute.GetProperty("specification_attribute_options")[0]);
            }
            else
            {
                targetId = Id((await Send(HttpMethod.Post, targetPath, HttpStatusCode.OK,
                    new { manufacturer = new { name, published = true } })).GetProperty("manufacturers")[0]);
                targetKey = targetId.Value;
            }

            object Body(int order) => specification
                ? new { product_specification_attribute = new { product_id = productId, specification_attribute_option_id = targetKey, display_order = order } }
                : new { product_manufacturer_mapping = new { product_id = productId, manufacturer_id = targetKey, display_order = order } };
            mappingId = Id((await Send(HttpMethod.Post, path, HttpStatusCode.OK, Body(17))).GetProperty(envelope)[0]);
            var replay = (await Send(HttpMethod.Post, path, HttpStatusCode.OK, Body(17))).GetProperty(envelope)[0];
            TestAssert.That(Id(replay), Is.EqualTo(mappingId.Value));
            await Send(HttpMethod.Post, path, HttpStatusCode.Conflict, Body(20));

            object update = specification
                ? new { product_specification_attribute = new { display_order = 23 } }
                : new { product_manufacturer_mapping = new { display_order = 23 } };
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var reordered = (await Send(HttpMethod.Put, $"{path}/{mappingId}", HttpStatusCode.OK, update))
                    .GetProperty(envelope)[0];
                TestAssert.That(reordered.GetProperty("display_order").GetInt32(), Is.EqualTo(23));
            }
        }
        finally
        {
            if (mappingId.HasValue)
                await Send(HttpMethod.Delete, $"{path}/{mappingId}", HttpStatusCode.OK);
            if (targetId.HasValue)
                await Send(HttpMethod.Delete, $"{targetPath}/{targetId}", HttpStatusCode.OK);
            await Send(HttpMethod.Delete, $"/api/products/{productId}", HttpStatusCode.OK);
        }

        static int Id(JsonElement item) => item.GetProperty("id").GetInt32();
    }
}
