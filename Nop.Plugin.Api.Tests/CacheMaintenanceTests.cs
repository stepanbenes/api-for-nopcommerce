using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Nop.Core.Caching;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Logging;
using Nop.Plugin.Api.Controllers;
using Nop.Services.Authentication;
using Nop.Services.Logging;

namespace Nop.Plugin.Api.Tests;

[TestFixture]
public class CacheMaintenanceTests
{
    [Test]
    public async Task ClearCacheAuditsCallerBeforeClearing()
    {
        var caller = new Customer { Id = 12 };
        var cache = new Mock<IStaticCacheManager>(MockBehavior.Strict);
        var activity = new Mock<ICustomerActivityService>(MockBehavior.Strict);
        var authentication = new Mock<IAuthenticationService>();
        authentication.Setup(service => service.GetAuthenticatedCustomerAsync()).ReturnsAsync(caller);
        var sequence = new MockSequence();
        activity.InSequence(sequence)
            .Setup(service => service.InsertActivityAsync(caller, "Api.ClearCache", It.IsAny<string>(), null))
            .ReturnsAsync(new ActivityLog());
        cache.InSequence(sequence).Setup(manager => manager.ClearAsync()).Returns(Task.CompletedTask);

        var response = await new CacheMaintenanceController(cache.Object, activity.Object, authentication.Object)
            .ClearCache();

        Assert.That(response, Is.TypeOf<NoContentResult>());
        activity.Verify(service => service.InsertActivityAsync(caller, "Api.ClearCache", It.IsAny<string>(), null), Times.Once);
        cache.Verify(manager => manager.ClearAsync(), Times.Once);
    }

    [Test]
    public async Task ClearCacheLeavesCacheIntactWhenActivityCannotBeRecorded()
    {
        var caller = new Customer { Id = 12 };
        var cache = new Mock<IStaticCacheManager>();
        var activity = new Mock<ICustomerActivityService>();
        var authentication = new Mock<IAuthenticationService>();
        authentication.Setup(service => service.GetAuthenticatedCustomerAsync()).ReturnsAsync(caller);
        activity.Setup(service => service.InsertActivityAsync(caller, "Api.ClearCache", It.IsAny<string>(), null))
            .ReturnsAsync((ActivityLog)null);
        var controller = new CacheMaintenanceController(cache.Object, activity.Object, authentication.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var response = await controller.ClearCache();

        Assert.That(response, Is.TypeOf<ObjectResult>());
        Assert.That(((ObjectResult)response).StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
        cache.Verify(manager => manager.ClearAsync(), Times.Never);
    }
}
