using Moq;
using Newtonsoft.Json.Linq;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Core.Infrastructure;
using Nop.Plugin.Api.Delta;
using Nop.Plugin.Api.DTO.Categories;
using Nop.Plugin.Api.DTO.Orders;
using Nop.Plugin.Api.MappingExtensions;
using Nop.Plugin.Api.Maps;
using NUnit.Framework;

namespace Nop.Plugin.Api.Tests;

[TestFixture]
[NonParallelizable]
public class DtoMappingTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void CategoryHomepageFlagRoundTrips(bool value)
    {
        var category = new Category { ShowOnHomepage = !value };
        CreateDelta<CategoryDto>("show_on_home_page", value).Merge(category);

        Assert.That(category.ShowOnHomepage, Is.EqualTo(value));
        Assert.That(JObject.FromObject(category.ToDto()).Value<bool?>("show_on_home_page"), Is.EqualTo(value));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void OrderPickupFlagRoundTrips(bool value)
    {
        var order = new Order { PickupInStore = !value };
        CreateDelta<OrderDto>("pick_up_in_store", value).Merge(order);

        Assert.That(order.PickupInStore, Is.EqualTo(value));
        Assert.That(JObject.FromObject(order.ToDto()).Value<bool?>("pick_up_in_store"), Is.EqualTo(value));
    }

    private static Delta<TDto> CreateDelta<TDto>(string property, bool value) where TDto : class, new()
    {
        var engine = new Mock<IEngine>();
        engine.Setup(instance => instance.Resolve<IJsonPropertyMapper>(null)).Returns(new JsonPropertyMapper());
        var previous = EngineContext.Current;
        EngineContext.Replace(engine.Object);
        try
        {
            return new Delta<TDto>(new Dictionary<string, object> { [property] = value });
        }
        finally
        {
            EngineContext.Replace(previous);
        }
    }
}
