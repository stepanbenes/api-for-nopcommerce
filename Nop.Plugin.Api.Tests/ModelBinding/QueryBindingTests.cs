using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Api.Attributes;
using Nop.Plugin.Api.Controllers;
using Nop.Plugin.Api.Converters;
using Nop.Plugin.Api.DTO.Errors;
using Nop.Plugin.Api.Infrastructure;
using Nop.Plugin.Api.ModelBinders;
using Nop.Plugin.Api.Models.CustomersParameters;
using Nop.Plugin.Api.Models.OrdersParameters;
using Nop.Services.Authentication;
using Nop.Services.Customers;
using NUnit.Framework;
using TestAssert = NUnit.Framework.Assert;

namespace Nop.Plugin.Api.Tests.ModelBinding;

[TestFixture]
public class QueryBindingTests
{
    [TestCase("CreatedAtMin=2008-12-31%2003:00")]
    [TestCase("createdAtMin=2008-12-31T03:00:00.0000000Z")]
    [TestCase("created_at_min=2008-12-31T04:30:00%2B01:30")]
    [TestCase("CREATED_AT_MIN=2008-12-30T23:00:00-04:00")]
    public async Task DateAliasesAndOffsetsBindTheSameUtcInstant(string query)
    {
        var customer = await BindAsync<CustomersParametersModel>(query);
        var order = await BindAsync<OrdersParametersModel>(query);
        var instant = new DateTime(2008, 12, 31, 3, 0, 0, DateTimeKind.Utc);

        TestAssert.That(customer.ModelState.IsValid && order.ModelState.IsValid, Is.True);
        var dates = new[] { ((CustomersParametersModel)customer.Result.Model).CreatedAtMin,
            ((OrdersParametersModel)order.Result.Model).CreatedAtMin };
        foreach (var date in dates)
        {
            TestAssert.That(date, Is.EqualTo(instant));
            TestAssert.That(date?.Kind, Is.EqualTo(DateTimeKind.Utc));
        }
    }

    [TestCase("created_at_min=bad")]
    [TestCase("created_at_min=")]
    [TestCase("created_at_min=2008-02-30")]
    [TestCase("created_at_min=2008-12-30&CreatedAtMin=2008-12-31")]
    [TestCase("created_at_min=2008-12-30&created_at_min=2008-12-31")]
    public async Task InvalidOrConflictingDatesReportTheCanonicalFilterName(string query)
    {
        var bound = await BindAsync<OrdersParametersModel>(query);
        TestAssert.That(bound.ModelState.IsValid, Is.False);
        TestAssert.That(bound.ModelState["created_at_min"].Errors, Is.Not.Empty);
    }

    [Test]
    public async Task ParallelRequestsKeepSeparateValuesAndThePluginRegistersNoSharedDictionary()
    {
        var services = new ServiceCollection();
        new DependencyRegister().ConfigureServices(services, null);
        TestAssert.That(services.Any(service => service.ServiceType == typeof(Dictionary<string, object>)), Is.False);

        var binder = new ParametersModelBinder<CustomersParametersModel>(new ObjectConverter(new ApiTypeConverter()));
        var requests = Enumerable.Range(1, 20).Select(day => Task.Run(async () =>
        {
            var bound = await BindAsync($"created_at_min=2008-12-{day:D2}&limit={day}", binder);
            TestAssert.That(bound.ModelState.IsValid, Is.True);
            var model = (CustomersParametersModel)bound.Result.Model;
            TestAssert.That(model.CreatedAtMin?.Day, Is.EqualTo(day));
            TestAssert.That(model.Limit, Is.EqualTo(day));
        }));

        await Task.WhenAll(requests);
    }

    [TestCase("", false, null, 50)]
    [TestCase("?unpaged=false&limit=2", false, null, 2)]
    [TestCase("?unpaged=true", true, null, int.MaxValue)]
    [TestCase("?unpaged=true", false, 403, null)]
    [TestCase("?unpaged=true&Page=1", true, 400, null)]
    [TestCase("?unpaged=true&limit=50", true, 400, null)]
    [TestCase("?unpaged=true&parameters.Page=2", true, 400, null)]
    [TestCase("?unpaged=bad", true, 400, null)]
    [TestCase("?unpaged=true&unpaged=false", true, 400, null)]
    public async Task OnlyAdministratorsCanOptInWithoutPagingParameters(string query, bool administrator,
        int? status, int? expectedLimit)
    {
        var customer = new Customer { Id = 1 };
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(service => service.GetAuthenticatedCustomerAsync()).ReturnsAsync(customer);
        var customers = new Mock<ICustomerService>();
        customers.Setup(service => service.IsAdminAsync(customer, true)).ReturnsAsync(administrator);
        using var services = new ServiceCollection().AddSingleton(auth.Object).AddSingleton(customers.Object).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.QueryString = new QueryString(query);
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var controller = new PageTestController { ControllerContext = new ControllerContext { HttpContext = http } };
        var context = new ActionExecutingContext(action, [], new Dictionary<string, object>(), controller);
        int? actualLimit = null;

        await new UnpagedCollectionAttribute().OnActionExecutionAsync(context, () =>
        {
            actualLimit = controller.EffectiveLimit(query.Contains("limit=2") ? 2 : null);
            return Task.FromResult(new ActionExecutedContext(action, [], controller));
        });

        TestAssert.That(actualLimit, Is.EqualTo(expectedLimit));
        if (status.HasValue)
        {
            var result = (ObjectResult)context.Result;
            TestAssert.That(result.StatusCode, Is.EqualTo(status));
            TestAssert.That(((ErrorsRootObject)result.Value).Errors.Keys, Contains.Item("unpaged"));
        }
        else
        {
            TestAssert.That(context.Result, Is.Null);
        }
    }

    private static async Task<DefaultModelBindingContext> BindAsync<T>(string query, ParametersModelBinder<T> binder = null)
        where T : class, new()
    {
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString("?" + query);
        var context = new DefaultModelBindingContext
        {
            ActionContext = new ActionContext(http, new RouteData(), new ActionDescriptor()),
            ModelState = new ModelStateDictionary()
        };
        binder ??= new ParametersModelBinder<T>(new ObjectConverter(new ApiTypeConverter()));
        await binder.BindModelAsync(context);
        return context;
    }

    private sealed class PageTestController() : BaseApiController(null, null, null, null, null, null, null, null, null)
    {
        public int EffectiveLimit(int? limit) => GetCollectionLimit(limit);
    }
}
