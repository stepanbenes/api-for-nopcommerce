using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Nop.Core.Configuration;
using Nop.Core.Infrastructure;
using Nop.Plugin.Api.Configuration;
using Nop.Plugin.Api.Controllers;
using Nop.Plugin.Api.Infrastructure;
using NUnit.Framework;
using Swashbuckle.AspNetCore.Swagger;
using TestAssert = NUnit.Framework.Assert;

namespace Nop.Plugin.Api.Tests;

[NonParallelizable]
public class ApiAudienceOpenApiTests
{
    [Test]
    public async Task RuntimeOperationAudiencesMatchTheCanonicalSnapshot()
    {
        var previousSettings = Singleton<AppSettings>.Instance;
        try
        {
            Singleton<AppSettings>.Instance = new AppSettings([new ApiConfiguration()]);
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddControllers()
                .AddApplicationPart(typeof(TokenController).Assembly)
                .AddNewtonsoftJson();
            new ApiStartup().ConfigureServices(builder.Services, builder.Configuration);

            await using var app = builder.Build();
            await using var snapshot = File.OpenRead(Path.Combine(TestContext.CurrentContext.TestDirectory, "nopapi.swagger.json"));
            using var json = await JsonDocument.ParseAsync(snapshot);
            var expected = json.RootElement.GetProperty("paths").EnumerateObject()
                .SelectMany(path => path.Value.EnumerateObject()
                    .Where(operation => Enum.TryParse<OperationType>(operation.Name, ignoreCase: true, out _))
                    .Select(operation =>
                        (Path: path.Name, Method: operation.Name, Audiences: operation.Value.GetProperty("x-audiences")
                            .EnumerateArray().Select(audience => audience.GetString()).ToArray())))
                .ToDictionary(operation => (operation.Path, operation.Method), operation => operation.Audiences);
            var live = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1").Paths
                .SelectMany(path => path.Value.Operations.Select(operation =>
                    (Path: path.Key, Method: operation.Key.ToString().ToLowerInvariant(),
                        Audiences: ((OpenApiArray)operation.Value.Extensions["x-audiences"])
                            .Cast<OpenApiString>().Select(audience => audience.Value).ToArray())))
                .ToDictionary(operation => (operation.Path, operation.Method), operation => operation.Audiences);

            TestAssert.That(live.Keys, Is.EquivalentTo(expected.Keys));
            foreach (var (key, audiences) in expected)
                TestAssert.That(live[key], Is.EqualTo(audiences), $"{key.Method.ToUpperInvariant()} {key.Path}");
        }
        finally
        {
            Singleton<AppSettings>.Instance = previousSettings;
        }
    }
}
